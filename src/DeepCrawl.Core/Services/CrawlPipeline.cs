using System.Text.Json;
using DeepCrawl.Core.Dtos;
using DeepCrawl.Core.Hashing;
using DeepCrawl.Domain.Abstractions;
using DeepCrawl.Domain.Entities;
using DeepCrawl.Domain.Enums;
using DeepCrawl.Domain.Models;
using FreeSql;
using Microsoft.Extensions.Logging;

namespace DeepCrawl.Core.Services;

public class CrawlPipeline(
    CleanPipeline cleanPipeline,
    IBaseRepository<CrawlRecord> crawlRecordRepo,
    IRedisClient redisClient,
    TieredHttpFetcher tieredFetcher,
    CrawlConfig crawlConfig,
    IBaseRepository<CrawlStatistic> crawlStatisticRepo,
    IDomainReporter domainReporter,
    IRobotsTxtService robotsTxtService,
    ILogger<CrawlPipeline> logger) : ICrawlPipeline
{
    private static readonly CacheKey CacheHitQueueKey = new("Stats", "CacheHitQueue");

    private CacheKey GetCacheKey(string contextHash, TimeSpan ttl)
        => new CacheKey("Crawl", contextHash, ttl);

    private TimeSpan ComputeTtl(int n)
    {
        var threshold = crawlConfig.CacheThreshold;
        double raw;
        if (n <= threshold)
        {
            raw = crawlConfig.CacheBaseMinutes * (1 + Math.Log2(Math.Max(n, 0) + 1));
        }
        else
        {
            var atThreshold = crawlConfig.CacheBaseMinutes * (1 + Math.Log2(threshold + 1));
            raw = atThreshold * Math.Pow(2, n - threshold);
        }
        raw = Math.Min(raw, crawlConfig.CacheMaxMinutes);
        var factor = 0.85 + Random.Shared.NextDouble() * 0.30;
        return TimeSpan.FromMinutes(raw * factor);
    }

    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(1);
    
    public async Task<ScrapeResponse> ScrapeAsync(ScrapeRequest request, CancellationToken ct = default)
    {
        logger.LogInformation("Scrape requested for {Url}", request.Url);

        var formats = request.Formats ?? new List<string> { "markdown" };
        var useAi = crawlConfig.AiConfigured && !request.SkipAiClean;
        var context = new CleanContext
        {
            Url = request.Url,
            UseAiClean = useAi,
            Formats = formats
        };

        var contextHash = context.ComputeContextHash();

        var cached = await redisClient.GetAsync<ScrapeResponse>(GetCacheKey(contextHash, DefaultTtl), ct);
        if (cached is not null)
        {
            logger.LogInformation("Redis cache hit for {Url}", request.Url);
            if (useAi)
                await redisClient.ListRightPushAsync(CacheHitQueueKey, request.Url, ct);
            return cached;
        }

        await using var handle = await redisClient
            .GetLock($"Crawl:{request.Url}")
            .AcquireAsync(TimeSpan.FromSeconds(180), ct);
        // double check
        cached = await redisClient.GetAsync<ScrapeResponse>(GetCacheKey(contextHash, DefaultTtl), ct);
        if (cached is not null)
        {
            logger.LogInformation("Redis cache hit after lock for {Url}", request.Url);
            if (useAi)
                await redisClient.ListRightPushAsync(CacheHitQueueKey, request.Url, ct);
            return cached;
        }

        var requestDomain = ExtractDomain(request.Url);
        if (requestDomain is not null)
        {
            var blockedKey = new CacheKey("Reputation", $"Blocked:{requestDomain}");
            if (await redisClient.ExistsAsync(blockedKey, ct))
            {
                var blockedUntilStr = await redisClient.GetAsync<string>(blockedKey, ct);
                DateTimeOffset? blockedUntil = null;
                if (blockedUntilStr is not null && DateTimeOffset.TryParse(blockedUntilStr, out var parsed))
                    blockedUntil = parsed;

                logger.LogInformation("Domain {Domain} is blocked, fast-failing {Url}", requestDomain, request.Url);
                return new ScrapeResponse
                {
                    Success = false,
                    Error = "The website refused the connection, likely due to strict anti-crawling measures. This domain will be temporarily avoided.",
                    BlockedUntil = blockedUntil
                };
            }
        }

        var (success, tier, rawHtml, fetchError) = await tieredFetcher.FetchAsync(
            request.Url, request.WaitUntil, ct);

        if (!success)
        {
            await domainReporter.RecordFailureAsync(request.Url, ct);
            var failedRecord = new CrawlRecord
            {
                Url = request.Url,
                Status = CrawlStatus.Failed,
                FetchTier = tier,
                ErrorMessage = fetchError ?? "All fetch tiers failed",
                CompletedAt = DateTime.UtcNow,
                LastAccessedAt = DateTime.UtcNow
            };
            await crawlRecordRepo.InsertAsync(failedRecord, ct);
            return new ScrapeResponse { Success = false, Error = fetchError ?? "All fetch tiers failed", CrawlRecordId = failedRecord.Id };
        }

        int? statusCode = 200;
        string contentType = "text/html";

        if (rawHtml is null)
        {
            await domainReporter.RecordFailureAsync(request.Url, ct);
            return new ScrapeResponse { Success = false, Error = "Fetched content was empty" };
        }

        var htmlHash = HtmlHashService.ComputeSha256(rawHtml);

        var sameHash = await crawlRecordRepo
            .Where(c => c.Url == request.Url && c.HtmlHash == htmlHash)
            .FirstAsync(ct);
        if (sameHash is not null)
        {
            sameHash.FetchTier = tier;
            sameHash.StabilityCount++;
            sameHash.LastAccessedAt = DateTime.UtcNow;
            await crawlRecordRepo.UpdateAsync(sameHash, ct);

            // If user wants AI but only raw markdown is cached (e.g. from pre-crawl),
            // fall through to the clean pipeline instead of returning raw content.
            if (useAi && sameHash.CleanedMarkdown is null)
            {
                logger.LogInformation("Hash match for {Url} but AI cleaning pending, re-cleaning", request.Url);
            }
            else
            {
                logger.LogInformation("Hash match, returning cached result for {Url}", request.Url);
                if (useAi)
                    await redisClient.ListRightPushAsync(CacheHitQueueKey, request.Url, ct);
                var cachedMd = useAi
                    ? sameHash.CleanedMarkdown!
                    : sameHash.MarkdownContent;

                var cachedMetadata = sameHash.MetadataJson is not null
                    ? JsonSerializer.Deserialize<CrawlMetadata>(sameHash.MetadataJson)
                    : null;

                var cacheResponse = BuildResponse(formats, cachedMd, sameHash.CleanedHtml, cachedMetadata, request.Url, statusCode, contentType)
                    with { CrawlRecordId = sameHash.Id };
                await redisClient.SetAsync(GetCacheKey(contextHash, ComputeTtl(sameHash.StabilityCount)), cacheResponse, ct);
                return cacheResponse;
            }
        }

        context.StatusCode = statusCode;
        context.ContentType = contentType;
        var cleanResult = await cleanPipeline.ExecuteAsync(rawHtml!, context, ct);

        var existing = await crawlRecordRepo
            .Where(c => c.Url == request.Url)
            .FirstAsync(ct);
        long recordId;
        if (existing is not null)
        {
            existing.FetchTier = tier;
            existing.StabilityCount = Math.Max(1, Math.Min(
                existing.StabilityCount / crawlConfig.CacheResetDivisor,
                crawlConfig.CacheResetCap));
            existing.HtmlHash = htmlHash;
            existing.ContextHash = contextHash;
            existing.MarkdownContent = cleanResult.Output;
            existing.CleanedMarkdown = cleanResult.AiCleaned ? cleanResult.Output : null;
            existing.CleanedHtml = cleanResult.CleanedHtml;
            existing.MetadataJson = cleanResult.Metadata is not null
                ? JsonSerializer.Serialize(cleanResult.Metadata)
                : null;
            existing.Status = CrawlStatus.Completed;
            existing.CompletedAt = DateTime.UtcNow;
            existing.LastAccessedAt = DateTime.UtcNow;
            await crawlRecordRepo.UpdateAsync(existing, ct);
            recordId = existing.Id;
        }
        else
        {
            var record = new CrawlRecord
            {
                Url = request.Url,
                HtmlHash = htmlHash,
                ContextHash = contextHash,
                MarkdownContent = cleanResult.Output,
                CleanedMarkdown = cleanResult.AiCleaned ? cleanResult.Output : null,
                CleanedHtml = cleanResult.CleanedHtml,
                MetadataJson = cleanResult.Metadata is not null
                    ? JsonSerializer.Serialize(cleanResult.Metadata)
                    : null,
                Status = CrawlStatus.Completed,
                FetchTier = tier,
                StabilityCount = 1,
                CompletedAt = DateTime.UtcNow,
                LastAccessedAt = DateTime.UtcNow
            };
            await crawlRecordRepo.InsertAsync(record, ct);
            recordId = record.Id;
        }

        if (cleanResult.AiCleaned && cleanResult.TokenUsage is not null)
        {
            await crawlStatisticRepo.InsertAsync(new CrawlStatistic
            {
                CrawlRecordId = recordId,
                ContentHash = cleanResult.ContentHash,
                CacheHitCount = 0,
                PromptTokens = cleanResult.TokenUsage.PromptTokens,
                CompletionTokens = cleanResult.TokenUsage.CompletionTokens,
                TotalTokens = cleanResult.TokenUsage.TotalTokens,
                ReasoningTokens = cleanResult.TokenUsage.ReasoningTokens,
                CacheHitTokens = cleanResult.TokenUsage.CacheHitTokens,
                CacheMissTokens = cleanResult.TokenUsage.CacheMissTokens,
                Model = cleanResult.TokenUsage.Model,
                CreatedAt = DateTime.UtcNow
            }, ct);
        }

        // Fetch robots.txt for the target origin using the known tier
        if (cleanResult.Metadata is not null)
        {
            var useProxy = tier is FetchTier.HttpClientProxy or FetchTier.CloakBrowserProxy;
            cleanResult.Metadata.RobotsTxt = await robotsTxtService.FetchAsync(request.Url, useProxy, ct);
        }

        var response = BuildResponse(formats, cleanResult.Output, cleanResult.CleanedHtml, cleanResult.Metadata, request.Url, statusCode, contentType)
            with { CrawlRecordId = recordId };
        var finalTtl = ComputeTtl(existing?.StabilityCount ?? 1);
        await redisClient.SetAsync(GetCacheKey(contextHash, finalTtl), response, ct);
        await domainReporter.RecordSuccessAsync(request.Url, ct);
        return response;
    }

    private static ScrapeResponse BuildResponse(IList<string> formats, string? markdown, string? cleanedHtml, CrawlMetadata? metadata, string url, int? statusCode, string? contentType)
    {
        metadata ??= new CrawlMetadata();
        metadata.SourceURL ??= url;
        metadata.StatusCode ??= statusCode;
        metadata.ContentType ??= contentType;

        return new ScrapeResponse
        {
            Success = true,
            Data = new ScrapeData
            {
                Markdown = formats.Contains("markdown", StringComparer.OrdinalIgnoreCase) ? markdown : null,
                Html = formats.Contains("html", StringComparer.OrdinalIgnoreCase) ? cleanedHtml : null,
                Metadata = metadata
            }
        };
    }

    private static string? ExtractDomain(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return uri.Host.ToLowerInvariant();
        if (Uri.TryCreate($"https://{url}", UriKind.Absolute, out uri))
            return uri.Host.ToLowerInvariant();
        return null;
    }

}
