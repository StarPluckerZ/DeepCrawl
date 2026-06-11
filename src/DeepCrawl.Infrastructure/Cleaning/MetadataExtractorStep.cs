using AngleSharp;
using AngleSharp.Dom;
using DeepCrawl.Domain.Abstractions;
using DeepCrawl.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace DeepCrawl.Infrastructure.Cleaning;

public class MetadataExtractorStep : ICleanStep
{
    private readonly ILogger<MetadataExtractorStep> _logger;

    public MetadataExtractorStep(ILogger<MetadataExtractorStep> logger)
    {
        _logger = logger;
    }

    public CleanStage Stage => CleanStage.Html;
    public int Order => 0;

    public async Task<CleanResult> CleanAsync(string input, CleanContext context, CancellationToken ct = default)
    {
        var metadata = new CrawlMetadata
        {
            SourceURL = context.Url,
            StatusCode = context.StatusCode,
            ContentType = context.ContentType
        };

        try
        {
            var config = Configuration.Default;
            var document = await BrowsingContext.New(config).OpenAsync(req => req.Content(input));

            if (document.Head is null)
                _logger.LogWarning("HTML document parsed without <head> element for {Url} — meta tags may be missed", context.Url);

            metadata.Title = document.Head?.QuerySelector("title")?.TextContent?.Trim();

            var htmlEl = document.DocumentElement;
            metadata.Language = htmlEl?.GetAttribute("lang") ?? htmlEl?.GetAttribute("xml:lang");

            metadata.Description = GetMeta(document, "description");
            metadata.Keywords = GetMeta(document, "keywords");
            metadata.Robots = GetMeta(document, "robots") ?? GetMeta(document, null, "robots");
            metadata.OgTitle = GetMeta(document, null, "og:title");
            metadata.OgDescription = GetMeta(document, null, "og:description");
            metadata.OgUrl = GetMeta(document, null, "og:url");
            metadata.OgImage = GetMeta(document, null, "og:image");
            metadata.OgSiteName = GetMeta(document, null, "og:site_name");
            metadata.OgLocaleAlternate = GetAllMeta(document, null, "og:locale:alternate");

            if (metadata.Robots is not null)
                _logger.LogDebug("Robots meta extracted for {Url}: '{Robots}'", context.Url, metadata.Robots);
            else
                _logger.LogDebug("No robots meta tag found for {Url}", context.Url);

            _logger.LogDebug("Metadata extracted for {Url}: title={Title}", context.Url, metadata.Title);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Metadata extraction failed for {Url}: {ErrorType} — {Message}",
                context.Url, ex.GetType().Name, ex.Message);
        }

        context.Metadata = metadata;

        return new CleanResult { Output = input, AiCleaned = false };
    }

    private static string? GetMeta(IDocument doc, string? name = null, string? property = null)
    {
        var selector = name is not null
            ? $"meta[name=\"{name}\"]"
            : $"meta[property=\"{property}\"]";

        // Standard location: <head>
        var headResult = doc.Head?.QuerySelector(selector)?.GetAttribute("content")?.Trim();
        if (!string.IsNullOrWhiteSpace(headResult))
            return headResult;

        // Fallback: search entire document (handles missing <head>, fragmented HTML, etc.)
        var docResult = doc.QuerySelector(selector)?.GetAttribute("content")?.Trim();
        return !string.IsNullOrWhiteSpace(docResult) ? docResult : null;
    }

    private static string? GetAllMeta(IDocument doc, string? name = null, string? property = null)
    {
        var selector = name is not null
            ? $"meta[name=\"{name}\"]"
            : $"meta[property=\"{property}\"]";

        // Standard location: <head>
        var headValues = doc.Head?.QuerySelectorAll(selector)
            .Select(e => e.GetAttribute("content")?.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();

        if (headValues is { Count: > 0 })
            return string.Join(", ", headValues);

        // Fallback: search entire document
        var docValues = doc.QuerySelectorAll(selector)
            .Select(e => e.GetAttribute("content")?.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v));

        return string.Join(", ", docValues ?? []);
    }
}
