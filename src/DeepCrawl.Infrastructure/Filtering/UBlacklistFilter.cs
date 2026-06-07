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
    private CacheKey DomainsTempKey => new("UBlacklist", "Domains:tmp", DefaultTtl);
    private CacheKey WhitelistTempKey => new("UBlacklist", "Whitelist:tmp", DefaultTtl);

    [GeneratedRegex(@"^\*://\*\.(.+?)/\*$")]
    private static partial Regex DomainPattern();

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
        _logger.LogInformation("Refreshing UBlacklist rules from {Url}", _options.SubscriptionUrl);

        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var whitelist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var httpClient = _httpClientFactory.CreateClient("UBlacklist");
            var response = await httpClient.GetAsync(_options.SubscriptionUrl, ct);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);

            while (await reader.ReadLineAsync(ct) is { } line)
            {
                line = line.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('!') || line.StartsWith('#'))
                    continue;

                var isWhitelist = line.StartsWith("@");
                var normalized = isWhitelist ? line[1..].Trim() : line;

                var match = DomainPattern().Match(normalized);
                if (match.Success)
                {
                    var domain = match.Groups[1].Value;
                    if (isWhitelist)
                        whitelist.Add(domain);
                    else
                        domains.Add(domain);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download UBlacklist rules");
            throw;
        }

        await _redis.DeleteAsync(DomainsTempKey, ct);
        await _redis.DeleteAsync(WhitelistTempKey, ct);

        if (domains.Count > 0)
            await _redis.SetAddAsync(DomainsTempKey, domains.ToArray(), ct);

        if (whitelist.Count > 0)
            await _redis.SetAddAsync(WhitelistTempKey, whitelist.ToArray(), ct);

        await _redis.RenameKeyAsync(DomainsTempKey, DomainsKey, ct);
        await _redis.RenameKeyAsync(WhitelistTempKey, WhitelistKey, ct);

        _logger.LogInformation("UBlacklist loaded: {Blocked} blocked, {Whitelisted} whitelisted", domains.Count, whitelist.Count);
    }

    public async Task<bool> IsBlockedAsync(string url, CancellationToken ct = default)
    {
        if (!_options.Enabled) return false;

        var uri = TryCreateUri(url);
        if (uri is null) return false;

        var domain = uri.Host.ToLowerInvariant();

        var whitelisted = await _redis.SetContainsAsync(WhitelistKey, domain);
        if (whitelisted) return false;

        return await _redis.SetContainsAsync(DomainsKey, domain);
    }

    private static Uri? TryCreateUri(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return uri;
        if (Uri.TryCreate($"https://{url}", UriKind.Absolute, out uri))
            return uri;
        return null;
    }
}

public class UBlacklistOptions
{
    public string SubscriptionUrl { get; set; } = "https://raw.githubusercontent.com/eallion/uBlacklist-subscription-compilation/main/uBlacklist.txt";
    public int UpdateIntervalHours { get; set; } = 168;
    public bool Enabled { get; set; } = true;
}
