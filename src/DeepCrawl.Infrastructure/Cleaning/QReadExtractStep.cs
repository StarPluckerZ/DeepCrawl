using DeepCrawl.Domain.Abstractions;
using DeepCrawl.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace DeepCrawl.Infrastructure.Cleaning;

/// <summary>
/// qRead paragraph-density content extraction step.
/// Runs in the Html stage after HTML cleaning, before data URI stripping.
/// Extracts the main article body from semi-cleaned HTML, reducing downstream
/// Markdown conversion and AI cleaning cost.
/// </summary>
public class QReadExtractStep(ILogger<QReadExtractStep> logger) : ICleanStep
{
    public CleanStage Stage => CleanStage.Html;
    public int Order => 15; // after AngleSharpHtmlCleanerStep (10), before StripDataUriStep (20)

    public Task<CleanResult> CleanAsync(string input, CleanContext context, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var before = input.Length;
        var extractor = new QReadContentExtractor();
        var extractResult = extractor.Extract(input, context.Metadata?.Title);

        if (extractResult.Extracted)
        {
            var after = extractResult.OutputHtml.Length;
            logger.LogInformation(
                "QReadExtract: {Before} -> {After} chars (removed {Diff}) [container={Tag}.{Class}, score={Score:F1}]",
                before, after, before - after,
                extractResult.ContainerTag, extractResult.ContainerClass, extractResult.Score);

            return Task.FromResult(new CleanResult { Output = extractResult.OutputHtml, AiCleaned = false });
        }

        logger.LogInformation(
            "QReadExtract: {Before} chars unchanged (no content block found: {Reason})",
            before, extractResult.Reason);

        return Task.FromResult(new CleanResult { Output = input, AiCleaned = false });
    }
}
