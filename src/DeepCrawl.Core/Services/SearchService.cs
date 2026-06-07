using System.Security.Cryptography;
using System.Text;
using DeepCrawl.Core.Dtos;
using DeepCrawl.Domain.Abstractions;
using DeepCrawl.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DeepCrawl.Core.Services;

public class SearchService(
    ISearchProvider searchProvider,
    IEnumerable<IUrlFilter> urlFilters,
    IRedisClient redis,
    SearchServiceOptions options,
    ILogger<SearchService> logger)
    : ISearchService
{
    private static readonly Dictionary<string, string> TbsToFreshness = new()
    {
        ["qdr:h"] = "oneDay",
        ["qdr:d"] = "oneDay",
        ["qdr:w"] = "oneWeek",
        ["qdr:m"] = "oneMonth",
        ["qdr:y"] = "oneYear"
    };

    public async Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken ct = default)
    {
        var cacheKey = ComputeCacheKey(request);
        var cacheTtl = TimeSpan.FromMinutes(options.CacheMinutes);
        var cache = new CacheKey("Search", cacheKey, cacheTtl);

        var cached = await redis.GetAsync<List<SearchProviderResult>>(cache, ct);
        if (cached is not null)
        {
            logger.LogDebug("Search cache hit for {Query}", request.Query);
            return await BuildResponseAsync(cached, ct);
        }

        await using var handle = await redis
            .GetLock($"SearchLock:{cacheKey}")
            .AcquireAsync(TimeSpan.FromSeconds(30), ct);

        cached = await redis.GetAsync<List<SearchProviderResult>>(cache, ct);
        if (cached is not null)
        {
            logger.LogDebug("Search cache hit after lock for {Query}", request.Query);
            return await BuildResponseAsync(cached, ct);
        }

        var freshness = ResolveFreshness(request.Tbs);
        var include = request.IncludeDomains is { Count: > 0 } ? string.Join("|", request.IncludeDomains) : null;
        var exclude = request.ExcludeDomains is { Count: > 0 } ? string.Join("|", request.ExcludeDomains) : null;

        var providerRequest = new SearchProviderRequest
        {
            Query = request.Query,
            Count = Math.Min(request.Limit ?? 10, options.MaxResultCount),
            Freshness = freshness,
            Include = include,
            Exclude = exclude
        };

        List<SearchProviderResult> rawResults;
        try
        {
            rawResults = await searchProvider.SearchAsync(providerRequest, ct);
            logger.LogInformation("Search completed by {Provider}: {Query} returned {Count} results",
                searchProvider.ProviderName, request.Query, rawResults.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Search provider failed for {Query}", request.Query);
            return new SearchResponse { Success = false, Error = "Search provider unavailable" };
        }

        await redis.SetAsync(cache, rawResults, ct);
        return await BuildResponseAsync(rawResults, ct);
    }

    private async Task<SearchResponse> BuildResponseAsync(List<SearchProviderResult> rawResults, CancellationToken ct)
    {
        var filtered = new List<SearchResult>(rawResults.Count);
        foreach (var r in rawResults)
        {
            var blocked = false;
            foreach (var filter in urlFilters)
            {
                if (await filter.IsBlockedAsync(r.Url, ct))
                {
                    blocked = true;
                    logger.LogDebug("Filtered URL {Url}", r.Url);
                    break;
                }
            }
            if (!blocked)
            {
                filtered.Add(new SearchResult(r.Title, r.Description, r.Url));
            }
        }

        var warning = filtered.Count < rawResults.Count
            ? $"{rawResults.Count - filtered.Count} results filtered"
            : null;

        return new SearchResponse
        {
            Success = true,
            Data = new SearchData { Web = filtered },
            Warning = warning
        };
    }

    private static string ResolveFreshness(string? tbs)
    {
        if (string.IsNullOrWhiteSpace(tbs)) return "noLimit";

        if (TbsToFreshness.TryGetValue(tbs, out var freshness))
            return freshness;

        if (tbs.StartsWith("cdr:1,cd_min:"))
        {
            var parts = tbs.Split(',');
            var min = parts.FirstOrDefault(p => p.StartsWith("cd_min:"))?["cd_min:".Length..];
            var max = parts.FirstOrDefault(p => p.StartsWith("cd_max:"))?["cd_max:".Length..];
            if (min is not null && max is not null)
                return $"{min}..{max}";
        }

        return "noLimit";
    }

    private static string ComputeCacheKey(SearchRequest request)
    {
        var sources = request.Sources is { Count: > 0 } ? string.Join(",", request.Sources) : "web";
        var raw = $"{request.Query}|{request.Country}|{request.Location}|{request.Tbs}|{sources}|{string.Join(",", request.IncludeDomains ?? [])}|{string.Join(",", request.ExcludeDomains ?? [])}|{request.Limit}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexStringLower(hash);
    }
}
