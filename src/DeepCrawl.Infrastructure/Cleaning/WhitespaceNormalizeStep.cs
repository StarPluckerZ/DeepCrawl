using System.Text.RegularExpressions;
using DeepCrawl.Domain.Abstractions;
using DeepCrawl.Domain.Enums;

namespace DeepCrawl.Infrastructure.Cleaning;

/// <summary>
/// Normalizes whitespace in markdown before AI cleaning to reduce token waste.
/// Collapses multiple spaces and trailing whitespace within lines.
/// Runs after ReverseMarkdown conversion, before AI cleaning.
/// </summary>
internal sealed partial class WhitespaceNormalizeStep : ICleanStep
{
    public CleanStage Stage => CleanStage.Markdown;
    public int Order => 15; // after ReverseMarkdownStep (10), before OpenAICleanStep (20)

    // Collapse 2+ horizontal spaces into a single space
    [GeneratedRegex(@" {2,}", RegexOptions.None)]
    private static partial Regex MultiSpaceRegex();

    // Remove trailing whitespace at end of each line
    [GeneratedRegex(@"[ \t]+$", RegexOptions.Multiline)]
    private static partial Regex TrailingWhitespaceRegex();

    // Collapse 2+ consecutive blank lines into a single blank line
    [GeneratedRegex(@"\n{2,}", RegexOptions.None)]
    private static partial Regex MultiNewlineRegex();

    public Task<CleanResult> CleanAsync(string input, CleanContext context, CancellationToken ct = default)
    {
        var text = MultiSpaceRegex().Replace(input, " ");
        text = TrailingWhitespaceRegex().Replace(text, "");
        text = MultiNewlineRegex().Replace(text, "\n\n");

        var result = new CleanResult { Output = text };
        return Task.FromResult(result);
    }
}
