using DeepCrawl.Core.Dtos;
using DeepCrawl.Core.Hashing;
using DeepCrawl.Domain.Abstractions;
using DeepCrawl.Domain.Entities;
using DeepCrawl.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DeepCrawl.Core.Services;

public class CrawlPipelineOptions
{
    public int DefaultTtlMinutes { get; set; } = 60;
    public bool AiConfigured { get; set; }
}

public class CrawlPipeline
{
    private readonly ICloakBrowserClient _client;
    private readonly CleanPipeline _cleanPipeline;
    private readonly ICrawlRepository _repository;
    private readonly ITokenValidator? _tokenValidator;
    private readonly IOptions<CrawlPipelineOptions> _options;
    private readonly ILogger<CrawlPipeline> _logger;

    public CrawlPipeline(
        ICloakBrowserClient client,
        CleanPipeline cleanPipeline,
        ICrawlRepository repository,
        IOptions<CrawlPipelineOptions> options,
        ILogger<CrawlPipeline> logger,
        ITokenValidator? tokenValidator = null)
    {
        _client = client;
        _cleanPipeline = cleanPipeline;
        _repository = repository;
        _options = options;
        _logger = logger;
        _tokenValidator = tokenValidator;
    }

    public async Task<ScrapeResponse> ScrapeAsync(ScrapeRequest request, string? token, CancellationToken ct = default)
    {
        if (_tokenValidator is not null && !await _tokenValidator.ValidateAsync(token, ct))
        {
            return new ScrapeResponse { Success = false, Error = "Unauthorized: Invalid or missing API token." };
        }

        _logger.LogInformation("Scrape requested for {Url}", request.Url);

        var formats = request.Formats ?? new List<string> { "markdown" };
        var useAi = _options.Value.AiConfigured;
        var context = new CleanContext
        {
            Url = request.Url,
            UseAiClean = useAi,
            Formats = formats
        };

        var contextHash = context.ComputeContextHash();

        var cached = await _repository.GetByUrlAsync(request.Url, ct);
        if (cached is { Status: nameof(CrawlStatus.Completed) } && IsCacheFresh(cached) && cached.ContextHash == contextHash)
        {
            cached.LastAccessedAt = DateTime.UtcNow;
            await _repository.UpsertAsync(cached, ct);
            _logger.LogInformation("Cache hit for {Url}", request.Url);

            var cachedMarkdown = useAi
                ? (cached.CleanedMarkdown ?? cached.MarkdownContent)
                : cached.MarkdownContent;

            var cachedMetadata = cached.MetadataJson is not null
                ? System.Text.Json.JsonSerializer.Deserialize<CrawlMetadata>(cached.MetadataJson)
                : null;

            return BuildResponse(formats, cachedMarkdown, cached.CleanedHtml, cachedMetadata, request.Url, 200, "text/html");
        }

        string rawHtml;
        int? statusCode = 200;
        string contentType = "text/html";
        try
        {
            rawHtml = await _client.FetchHtmlAsync(request.Url, request.WaitUntil, request.Proxy, ct);
        }
        catch (CloakBrowserException ex)
        {
            return new ScrapeResponse { Success = false, Error = ex.Message };
        }

        var htmlHash = HtmlHashService.ComputeSha256(rawHtml);

        var sameHash = await _repository.GetByUrlAndHashAsync(request.Url, htmlHash, contextHash, ct);
        if (sameHash is not null)
        {
            sameHash.LastAccessedAt = DateTime.UtcNow;
            await _repository.UpsertAsync(sameHash, ct);
            _logger.LogInformation("Hash match, returning cached result for {Url}", request.Url);

            var cachedMd = useAi
                ? (sameHash.CleanedMarkdown ?? sameHash.MarkdownContent)
                : sameHash.MarkdownContent;

            var cachedMetadata2 = sameHash.MetadataJson is not null
                ? System.Text.Json.JsonSerializer.Deserialize<CrawlMetadata>(sameHash.MetadataJson)
                : null;

            return BuildResponse(formats, cachedMd, sameHash.CleanedHtml, cachedMetadata2, request.Url, statusCode, contentType);
        }

        context.StatusCode = statusCode;
        context.ContentType = contentType;
        var cleanResult = await _cleanPipeline.ExecuteAsync(rawHtml, context, ct);

        var record = new CrawlRecord
        {
            Url = request.Url,
            HtmlHash = htmlHash,
            ContextHash = contextHash,
            MarkdownContent = cleanResult.Output,
            CleanedMarkdown = cleanResult.AiCleaned ? cleanResult.Output : null,
            CleanedHtml = cleanResult.CleanedHtml,
            MetadataJson = cleanResult.Metadata is not null
                ? System.Text.Json.JsonSerializer.Serialize(cleanResult.Metadata)
                : null,
            Status = CrawlStatus.Completed.ToString(),
            CreatedAt = cached?.CreatedAt ?? DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            LastAccessedAt = DateTime.UtcNow
        };

        await _repository.UpsertAsync(record, ct);

        return BuildResponse(formats, cleanResult.Output, cleanResult.CleanedHtml, cleanResult.Metadata, request.Url, statusCode, contentType);
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

    public async Task<CrawlResponse?> GetContentAsync(string url, CancellationToken ct = default)
    {
        var cached = await _repository.GetByUrlAsync(url, ct);
        if (cached is { Status: nameof(CrawlStatus.Completed) })
        {
            return new CrawlResponse
            {
                Url = url,
                Markdown = cached.CleanedMarkdown ?? cached.MarkdownContent,
                Status = CrawlStatus.Completed.ToString(),
                FromCache = true,
                AiCleaned = cached.CleanedMarkdown is not null
            };
        }
        return null;
    }

    public async Task<CrawlResponse?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var record = await _repository.GetByIdAsync(id, ct);
        if (record is null) return null;

        return new CrawlResponse
        {
            Url = record.Url,
            Markdown = record.CleanedMarkdown ?? record.MarkdownContent,
            Status = record.Status,
            FromCache = true,
            AiCleaned = record.CleanedMarkdown is not null,
            ErrorCode = record.ErrorCode,
            ErrorMessage = record.ErrorMessage
        };
    }

    private bool IsCacheFresh(CrawlRecord record)
    {
        if (record.CompletedAt is null) return false;
        var ttl = TimeSpan.FromMinutes(_options.Value.DefaultTtlMinutes);
        return DateTime.UtcNow - record.CompletedAt.Value < ttl;
    }
}
