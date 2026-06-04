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
    ILogger<CrawlPipeline> logger) : ICrawlPipeline
{
    private CacheKey GetCacheKey(string contextHash) => new CacheKey("Crawl", contextHash, TimeSpan.FromHours(1));
    
    public async Task<ScrapeResponse> ScrapeAsync(ScrapeRequest request, CancellationToken ct = default)
    {
        logger.LogInformation("Scrape requested for {Url}", request.Url);

        var formats = request.Formats ?? new List<string> { "markdown" };
        var useAi = crawlConfig.AiConfigured;
        var context = new CleanContext
        {
            Url = request.Url,
            UseAiClean = useAi,
            Formats = formats
        };

        var contextHash = context.ComputeContextHash();

        var cached = await redisClient.GetAsync<ScrapeResponse>(GetCacheKey(contextHash), ct);
        if (cached is not null)
        {
            logger.LogInformation("Redis cache hit for {Url}", request.Url);
            return cached;
        }

        await using var handle = await redisClient
            .GetLock($"Crawl:{request.Url}")
            .AcquireAsync(TimeSpan.FromSeconds(60), ct);
        // double check
        cached = await redisClient.GetAsync<ScrapeResponse>(GetCacheKey(contextHash), ct);
        if (cached is not null)
        {
            logger.LogInformation("Redis cache hit after lock for {Url}", request.Url);
            return cached;
        }

        var (success, tier, rawHtml, fetchError) = await tieredFetcher.FetchAsync(
            request.Url, request.WaitUntil, ct);

        if (!success)
        {
            await crawlRecordRepo.InsertAsync(new CrawlRecord
            {
                Url = request.Url,
                Status = CrawlStatus.Failed,
                FetchTier = tier,
                ErrorMessage = fetchError ?? "All fetch tiers failed",
                CompletedAt = DateTime.Now,
                LastAccessedAt = DateTime.Now
            }, ct);
            return new ScrapeResponse { Success = false, Error = fetchError ?? "All fetch tiers failed" };
        }

        int? statusCode = 200;
        string contentType = "text/html";

        var htmlHash = HtmlHashService.ComputeSha256(rawHtml!);

        var sameHash = await crawlRecordRepo
            .Where(c => c.Url == request.Url && c.HtmlHash == htmlHash)
            .FirstAsync(ct);
        if (sameHash is not null)
        {
            sameHash.FetchTier = tier;
            sameHash.LastAccessedAt = DateTime.Now;
            await crawlRecordRepo.UpdateAsync(sameHash, ct);
            logger.LogInformation("Hash match, returning cached result for {Url}", request.Url);

            var cachedMd = useAi
                ? (sameHash.CleanedMarkdown ?? sameHash.MarkdownContent)
                : sameHash.MarkdownContent;

            var cachedMetadata = sameHash.MetadataJson is not null
                ? JsonSerializer.Deserialize<CrawlMetadata>(sameHash.MetadataJson)
                : null;

            var cacheResponse = BuildResponse(formats, cachedMd, sameHash.CleanedHtml, cachedMetadata, request.Url, statusCode, contentType);
            await redisClient.SetAsync(GetCacheKey(contextHash), cacheResponse, ct);
            return cacheResponse;
        }

        context.StatusCode = statusCode;
        context.ContentType = contentType;
        var cleanResult = await cleanPipeline.ExecuteAsync(rawHtml!, context, ct);

        var existing = await crawlRecordRepo
            .Where(c => c.Url == request.Url)
            .FirstAsync(ct);
        if (existing is not null)
        {
            existing.FetchTier = tier;
            existing.HtmlHash = htmlHash;
            existing.ContextHash = contextHash;
            existing.MarkdownContent = cleanResult.Output;
            existing.CleanedMarkdown = cleanResult.AiCleaned ? cleanResult.Output : null;
            existing.CleanedHtml = cleanResult.CleanedHtml;
            existing.MetadataJson = cleanResult.Metadata is not null
                ? JsonSerializer.Serialize(cleanResult.Metadata)
                : null;
            existing.Status = CrawlStatus.Completed;
            existing.CompletedAt = DateTime.Now;
            existing.LastAccessedAt = DateTime.Now;
            await crawlRecordRepo.UpdateAsync(existing, ct);
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
                CompletedAt = DateTime.Now,
                LastAccessedAt = DateTime.Now
            };
            await crawlRecordRepo.InsertAsync(record, ct);
        }

        var response = BuildResponse(formats, cleanResult.Output, cleanResult.CleanedHtml, cleanResult.Metadata, request.Url, statusCode, contentType);
        await redisClient.SetAsync(GetCacheKey(contextHash), response, ct);
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

}
