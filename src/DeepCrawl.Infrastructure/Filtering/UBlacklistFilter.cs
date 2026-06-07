using System.Text.Json;
using System.Text.RegularExpressions;
using DeepCrawl.Domain.Abstractions;
using DeepCrawl.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DeepCrawl.Infrastructure.Filtering;

public partial class UBlacklistFilter : IUrlFilter
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IRedisClient _redis;
    private readonly UBlacklistOptions _options;
    private readonly ILogger<UBlacklistFilter> _logger;

    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(168);

    private CacheKey DomainsKey => new("UBlacklist", "Domains", DefaultTtl);
    private CacheKey WhitelistKey => new("UBlacklist", "Whitelist", DefaultTtl);
    private CacheKey ExactDomainsKey => new("UBlacklist", "ExactDomains", DefaultTtl);
    private CacheKey PathRulesKey => new("UBlacklist", "PathRules", DefaultTtl);
    private CacheKey RegexRulesKey => new("UBlacklist", "RegexRules", DefaultTtl);

    // temp keys for atomic swap
    private CacheKey DomainsTempKey => new("UBlacklist", "Domains:tmp", DefaultTtl);
    private CacheKey WhitelistTempKey => new("UBlacklist", "Whitelist:tmp", DefaultTtl);
    private CacheKey ExactDomainsTempKey => new("UBlacklist", "ExactDomains:tmp", DefaultTtl);

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private volatile PathRuleCache? _pathRuleCache;
    private volatile Regex[]? _regexCache;

    private readonly object _cacheLock = new();

    [GeneratedRegex(@"^\*://\*\.([^/]+)/\*$")]
    private static partial Regex WildcardPattern();

    [GeneratedRegex(@"^\*://([^/]+)/\*$")]
    private static partial Regex ExactDomainPattern();

    [GeneratedRegex(@"^\*://(\*\.)?(.+?)(/.+)$")]
    private static partial Regex PathPattern();

    [GeneratedRegex(@"^/(.+)/$")]
    private static partial Regex RegexRulePattern();

    public UBlacklistFilter(
        IHttpClientFactory httpClientFactory,
        IRedisClient redis,
        UBlacklistOptions options,
        ILogger<UBlacklistFilter> logger)
    {
        _httpClientFactory = httpClientFactory;
        _redis = redis;
        _options = options;
        _logger = logger;
    }

    public async Task LoadRulesAsync(CancellationToken ct = default)
    {
        if (await _redis.ExistsAsync(DomainsKey, ct))
        {
            _logger.LogInformation("UBlacklist rules already loaded in Redis");
            return;
        }

        await RefreshAsync(ct);
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Refreshing UBlacklist rules from {Count} sources", _options.SubscriptionUrls.Count);

        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var whitelist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var exactDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pathRules = new List<PathRuleEntry>();
        var regexRules = new List<string>();

        using var httpClient = _httpClientFactory.CreateClient("UBlacklist");

        foreach (var url in _options.SubscriptionUrls)
        {
            try
            {
                _logger.LogDebug("Downloading UBlacklist source: {Url}", url);
                var response = await httpClient.GetAsync(url, ct);
                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var reader = new StreamReader(stream);

                var lineNo = 0;
                while (await reader.ReadLineAsync(ct) is { } line)
                {
                    lineNo++;
                    line = line.Trim();
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith('!') || line.StartsWith('#'))
                        continue;

                    var isWhitelist = line.StartsWith("@");
                    var normalized = isWhitelist ? line[1..].Trim() : line;

                    try
                    {
                        ParseRule(normalized, isWhitelist, domains, whitelist, exactDomains, pathRules, regexRules);
                    }
                    catch
                    {
                        _logger.LogWarning("Skipped unparseable rule at {Url}:{LineNo}: {Line}", url, lineNo, line);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download UBlacklist source: {Url}", url);
            }
        }

        if (domains.Count == 0 && exactDomains.Count == 0 && pathRules.Count == 0 && regexRules.Count == 0)
        {
            _logger.LogWarning("UBlacklist refresh produced no rules — all sources may be unavailable");
            return;
        }

        await _redis.DeleteAsync(DomainsTempKey, ct);
        await _redis.DeleteAsync(WhitelistTempKey, ct);
        await _redis.DeleteAsync(ExactDomainsTempKey, ct);

        if (domains.Count > 0)
            await _redis.SetAddAsync(DomainsTempKey, domains.ToArray(), ct);

        if (whitelist.Count > 0)
            await _redis.SetAddAsync(WhitelistTempKey, whitelist.ToArray(), ct);

        if (exactDomains.Count > 0)
            await _redis.SetAddAsync(ExactDomainsTempKey, exactDomains.ToArray(), ct);

        await _redis.RenameKeyAsync(DomainsTempKey, DomainsKey, ct);
        await _redis.RenameKeyAsync(WhitelistTempKey, WhitelistKey, ct);
        await _redis.RenameKeyAsync(ExactDomainsTempKey, ExactDomainsKey, ct);

        var pathJson = JsonSerializer.Serialize(pathRules, JsonOpts);
        await _redis.SetAsync(PathRulesKey, pathJson, ct);

        var regexJson = JsonSerializer.Serialize(regexRules, JsonOpts);
        await _redis.SetAsync(RegexRulesKey, regexJson, ct);

        lock (_cacheLock)
        {
            _pathRuleCache = null;
            _regexCache = null;
        }

        _logger.LogInformation(
            "UBlacklist loaded: {Domains} domains, {Exact} exact, {Paths} path rules, {Regex} regex, {Whitelisted} whitelisted",
            domains.Count, exactDomains.Count, pathRules.Count, regexRules.Count, whitelist.Count);
    }

    private static void ParseRule(
        string rule,
        bool isWhitelist,
        HashSet<string> domains,
        HashSet<string> whitelist,
        HashSet<string> exactDomains,
        List<PathRuleEntry> pathRules,
        List<string> regexRules)
    {
        var regexMatch = RegexRulePattern().Match(rule);
        if (regexMatch.Success)
        {
            var pattern = regexMatch.Groups[1].Value;
            try
            {
                _ = new Regex(pattern, RegexOptions.IgnoreCase);
            }
            catch
            {
                return;
            }
            regexRules.Add(pattern);
            return;
        }

        var wildcardMatch = WildcardPattern().Match(rule);
        if (wildcardMatch.Success)
        {
            var domain = wildcardMatch.Groups[1].Value;
            if (isWhitelist)
                whitelist.Add(domain);
            else
                domains.Add(domain);
            return;
        }

        var exactMatch = ExactDomainPattern().Match(rule);
        if (exactMatch.Success)
        {
            var host = exactMatch.Groups[1].Value;
            exactDomains.Add(host.ToLowerInvariant());
            return;
        }

        var pathMatch = PathPattern().Match(rule);
        if (pathMatch.Success)
        {
            var host = pathMatch.Groups[2].Value.ToLowerInvariant();
            var path = pathMatch.Groups[3].Value;
            pathRules.Add(new PathRuleEntry(host, path));
        }
    }

    public async Task<bool> IsBlockedAsync(string url, CancellationToken ct = default)
    {
        if (!_options.Enabled) return false;

        var uri = TryCreateUri(url);
        if (uri is null) return false;

        var domain = uri.Host.ToLowerInvariant();
        var path = uri.AbsolutePath.ToLowerInvariant();

        var whitelisted = await _redis.SetContainsAsync(WhitelistKey, domain);
        if (whitelisted) return false;

        if (await _redis.SetContainsAsync(DomainsKey, domain))
            return true;

        if (await _redis.SetContainsAsync(ExactDomainsKey, domain))
            return true;

        if (await CheckPathRulesAsync(domain, path))
            return true;

        if (await CheckRegexAsync(url))
            return true;

        return false;
    }

    private async Task<bool> CheckPathRulesAsync(string domain, string path)
    {
        var cache = _pathRuleCache;
        if (cache is null)
        {
            var json = await _redis.GetAsync<string>(PathRulesKey);
            if (json is null) return false;

            var entries = JsonSerializer.Deserialize<List<PathRuleEntry>>(json, JsonOpts);
            if (entries is null) return false;

            cache = new PathRuleCache();
            foreach (var e in entries)
            {
                if (!cache.Rules.TryGetValue(e.Host, out var paths))
                {
                    paths = new List<PathRule>();
                    cache.Rules[e.Host] = paths;
                }

                var isPrefix = e.Path.EndsWith("/*");
                var normalized = isPrefix ? e.Path[..^2] : e.Path;
                paths.Add(new PathRule(normalized, isPrefix));
            }

            lock (_cacheLock)
            {
                _pathRuleCache = cache;
            }
        }

        if (!cache.Rules.TryGetValue(domain, out var rules))
            return false;

        foreach (var rule in rules)
        {
            if (rule.IsPrefix && path.StartsWith(rule.Path, StringComparison.OrdinalIgnoreCase))
                return true;
            if (!rule.IsPrefix && string.Equals(path, rule.Path, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private async Task<bool> CheckRegexAsync(string url)
    {
        var regexes = _regexCache;
        if (regexes is null)
        {
            var json = await _redis.GetAsync<string>(RegexRulesKey);
            if (json is null) return false;

            var patterns = JsonSerializer.Deserialize<List<string>>(json, JsonOpts);
            if (patterns is null || patterns.Count == 0) return false;

            regexes = new Regex[patterns.Count];
            for (var i = 0; i < patterns.Count; i++)
            {
                try
                {
                    regexes[i] = new Regex(patterns[i], RegexOptions.IgnoreCase | RegexOptions.Compiled);
                }
                catch
                {
                    regexes[i] = new Regex("^$");
                }
            }

            lock (_cacheLock)
            {
                _regexCache = regexes;
            }
        }

        foreach (var re in regexes)
        {
            if (re.IsMatch(url))
                return true;
        }

        return false;
    }

    private static Uri? TryCreateUri(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return uri;
        if (Uri.TryCreate($"https://{url}", UriKind.Absolute, out uri))
            return uri;
        return null;
    }

    private class PathRuleCache
    {
        public Dictionary<string, List<PathRule>> Rules { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private record PathRule(string Path, bool IsPrefix);

    private record PathRuleEntry(string Host, string Path);
}

public class UBlacklistOptions
{
    public List<string> SubscriptionUrls { get; set; } = [];
    public int UpdateIntervalHours { get; set; } = 168;
    public bool Enabled { get; set; } = true;
}
