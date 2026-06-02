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
    ICloakBrowserClient client,
    CleanPipeline cleanPipeline,
    IBaseRepository<CrawlRecord> crawlRecordRepo,
    IRedisClient redisClient,
    IDirectHttpFetcher directFetcher,
    IContentAnalyzer contentAnalyzer,
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

        var proxyConfigured = crawlConfig.ProxyConfigured;
        var proxyUrl = crawlConfig.ProxyUrl;

        string? rawHtml = null;
        var success = false;
        string? lastError = null;

        // Tier 1: HttpClient direct
        try
        {
            rawHtml = await directFetcher.FetchDirectAsync(request.Url, ct);
            if (contentAnalyzer.GetTextLength(rawHtml) >= crawlConfig.MinTextLength)
                success = true;
            else
                logger.LogDebug("Tier 1 (HttpClient) got JS skeleton for {Url}", request.Url);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug("Tier 1 (HttpClient) failed for {Url}: {Msg}", request.Url, ex.Message);
        }

        // Tier 2: HttpClient + proxy
        if (!success && proxyConfigured)
        {
            try
            {
                rawHtml = await directFetcher.FetchWithProxyAsync(request.Url, ct);
                if (contentAnalyzer.GetTextLength(rawHtml) >= crawlConfig.MinTextLength)
                    success = true;
                else
                    logger.LogDebug("Tier 2 (HttpClient+proxy) got JS skeleton for {Url}", request.Url);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug("Tier 2 (HttpClient+proxy) failed for {Url}: {Msg}", request.Url, ex.Message);
            }
        }

        // Tier 3: Cloak browser
        if (!success)
        {
            try
            {
                rawHtml = await client.FetchHtmlAsync(request.Url, request.WaitUntil, null, ct);
                success = !string.IsNullOrWhiteSpace(rawHtml);
            }
            catch (CloakBrowserException ex)
            {
                lastError = ex.Message;
                logger.LogDebug("Tier 3 (Cloak) failed for {Url}: {Msg}", request.Url, ex.Message);
            }
        }

        // Tier 4: Cloak browser + proxy
        if (!success && proxyConfigured)
        {
            try
            {
                rawHtml = await client.FetchHtmlAsync(request.Url, request.WaitUntil, proxyUrl, ct);
                success = !string.IsNullOrWhiteSpace(rawHtml);
            }
            catch (CloakBrowserException ex)
            {
                lastError = ex.Message;
                logger.LogDebug("Tier 4 (Cloak+proxy) failed for {Url}: {Msg}", request.Url, ex.Message);
            }
        }

        if (!success)
            return new ScrapeResponse { Success = false, Error = lastError ?? "All fetch tiers failed" };

        int? statusCode = 200;
        string contentType = "text/html";

        var htmlHash = HtmlHashService.ComputeSha256(rawHtml!);

        var sameHash = await crawlRecordRepo
            .Where(c => c.Url == request.Url && c.HtmlHash == htmlHash)
            .FirstAsync(ct);
        if (sameHash is not null)
        {
            sameHash.LastAccessedAt = DateTime.Now;
            await crawlRecordRepo.UpdateAsync(sameHash, ct);
            logger.LogInformation("Hash match, returning cached result for {Url}", request.Url);

            var cachedMd = useAi
                ? (sameHash.CleanedMarkdown ?? sameHash.MarkdownContent)
                : sameHash.MarkdownContent;

            var cachedMetadata2 = sameHash.MetadataJson is not null
                ? JsonSerializer.Deserialize<CrawlMetadata>(sameHash.MetadataJson)
                : null;

            var cacheResponse = BuildResponse(formats, cachedMd, sameHash.CleanedHtml, cachedMetadata2, request.Url, statusCode, contentType);
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
