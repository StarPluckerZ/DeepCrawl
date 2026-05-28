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

            metadata.Title = document.Head?.QuerySelector("title")?.TextContent?.Trim();

            var htmlEl = document.DocumentElement;
            metadata.Language = htmlEl?.GetAttribute("lang") ?? htmlEl?.GetAttribute("xml:lang");

            metadata.Description = GetMeta(document, "description");
            metadata.Keywords = GetMeta(document, "keywords");
            metadata.Robots = GetMeta(document, "robots");
            metadata.OgTitle = GetMeta(document, null, "og:title");
            metadata.OgDescription = GetMeta(document, null, "og:description");
            metadata.OgUrl = GetMeta(document, null, "og:url");
            metadata.OgImage = GetMeta(document, null, "og:image");
            metadata.OgSiteName = GetMeta(document, null, "og:site_name");
            metadata.OgLocaleAlternate = GetAllMeta(document, null, "og:locale:alternate");

            _logger.LogDebug("Metadata extracted for {Url}: title={Title}", context.Url, metadata.Title);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Metadata extraction failed for {Url}", context.Url);
        }

        context.Metadata = metadata;

        return new CleanResult { Output = input, AiCleaned = false };
    }

    private static string? GetMeta(IDocument doc, string? name = null, string? property = null)
    {
        var selector = name is not null
            ? $"meta[name=\"{name}\"]"
            : $"meta[property=\"{property}\"]";

        return doc.Head?.QuerySelector(selector)?.GetAttribute("content")?.Trim();
    }

    private static string? GetAllMeta(IDocument doc, string? name = null, string? property = null)
    {
        var selector = name is not null
            ? $"meta[name=\"{name}\"]"
            : $"meta[property=\"{property}\"]";

        var values = doc.Head?.QuerySelectorAll(selector)
            .Select(e => e.GetAttribute("content")?.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v));

        return string.Join(", ", values ?? []);
    }
}
