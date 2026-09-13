using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace DeepCrawl.Infrastructure.Cleaning;

/// <summary>
/// Result of qRead content extraction.
/// </summary>
public record ExtractResult(
    string OutputHtml,
    bool Extracted,
    double Score,
    string? Reason,
    string? ContainerTag,
    string? ContainerClass
);

/// <summary>
/// Paragraph-density-based content extraction for Chinese web pages.
/// Implements the qRead algorithm adapted for DOM-level HTML processing.
///
/// Core idea: body text regions have high character-per-line density,
/// while navigation, ads, and footers have low density (few chars per line).
/// </summary>
public class QReadContentExtractor
{
    // Block-level elements that are candidates for content extraction
    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "div", "section", "article", "main", "p",
        "li", "td", "th", "blockquote", "pre",
        "h1", "h2", "h3", "h4", "h5", "h6",
        "ul", "ol", "dl", "dd", "dt", "figcaption"
    };

    // Elements that represent a "line" when counting lineCount for density
    private static readonly HashSet<string> LineTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "li", "td", "th", "blockquote", "pre",
        "h1", "h2", "h3", "h4", "h5", "h6",
        "section", "article", "header", "footer", "nav", "aside",
        "dd", "dt", "figcaption", "form"
    };

    // Class/id keywords indicating non-content boilerplate (heavily penalized)
    private static readonly string[] NoisePatterns =
    [
        "comment", "sidebar", "footer", "widget", "nav", "menu",
        "related", "recommend", "hot", "trending", "share", "social",
        "copyright", "breadcrumb", "pagination", "advertisement",
        "sponsor", "promo", "banner", "popup", "modal"
    ];

    // Minimum thresholds for extraction
    private const double MinScore = 5.0;
    private const int MinTextChars = 50;

    /// <summary>
    /// Attempts to extract the main content from cleaned HTML using the qRead algorithm.
    /// </summary>
    /// <param name="cleanedHtml">Semi-cleaned HTML (after scripts, styles, nav, footer removed).</param>
    /// <param name="title">Optional page title for similarity scoring.</param>
    /// <returns>Extraction result with the content HTML or the original input on failure.</returns>
    public ExtractResult Extract(string cleanedHtml, string? title)
    {
        if (string.IsNullOrWhiteSpace(cleanedHtml))
            return new ExtractResult(cleanedHtml, false, 0, "Empty input", null, null);

        var parser = new HtmlParser();
        var document = parser.ParseDocument(cleanedHtml);

        try
        {
            if (document.Body is null)
                return new ExtractResult(cleanedHtml, false, 0, "No body element", null, null);

            // Score all candidate elements
            var candidates = new List<(IElement Element, double Score, int TextChars, double Density)>();
            ScoreElements(document.Body, title, candidates);

            if (candidates.Count == 0)
                return new ExtractResult(cleanedHtml, false, 0, "No block-level candidates found", null, null);

            // Sort by score descending, then by text chars descending (tiebreaker)
            candidates.Sort((a, b) =>
            {
                var scoreCmp = b.Score.CompareTo(a.Score);
                return scoreCmp != 0 ? scoreCmp : b.TextChars.CompareTo(a.TextChars);
            });

            var best = candidates[0];

            // Walk up from best candidate to find the appropriate container.
            // Short paragraphs (e.g., dialogue in Chinese novels) may have the
            // highest density but too few chars — their parent container usually
            // captures the full article.
            var selected = best.Element;
            var parent = selected.ParentElement;
            while (parent is not null && parent != document.Body)
            {
                var (parentScore, parentTextChars, parentDensity) = ScoreElement(parent, title);
                // Prefer parent if it has meaningfully more text AND
                // score is within 30% of current best
                if (parentTextChars > best.TextChars * 1.15 && parentScore >= best.Score * 0.7)
                {
                    selected = parent;
                    best = (selected, parentScore, parentTextChars, parentDensity);
                }

                parent = parent.ParentElement;
            }

            // If the selected element still has too few chars, walk up to the
            // nearest ancestor that passes the minimum text threshold.
            while (best.TextChars < MinTextChars && selected.ParentElement is not null
                                                 && selected.ParentElement != document.Body)
            {
                selected = selected.ParentElement;
                var (upScore, upTextChars, upDensity) = ScoreElement(selected, title);
                best = (selected, upScore, upTextChars, upDensity);
            }

            if (best.Score < MinScore || best.TextChars < MinTextChars)
            {
                return new ExtractResult(cleanedHtml, false, best.Score,
                    $"Best candidate score={best.Score:F1} density={best.Density:F1} chars={best.TextChars} below threshold",
                    best.Element.TagName, best.Element.ClassName);
            }

            var outputHtml = selected.InnerHtml;
            return new ExtractResult(outputHtml, true, best.Score, null,
                selected.TagName, selected.ClassName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ExtractResult(cleanedHtml, false, 0, $"Parse error: {ex.Message}", null, null);
        }
        finally
        {
            document.Dispose();
        }
    }

    private void ScoreElements(IElement element, string? title,
        List<(IElement Element, double Score, int TextChars, double Density)> candidates)
    {
        foreach (var child in element.Children)
        {
            if (BlockTags.Contains(child.TagName))
            {
                var (score, textChars, density) = ScoreElement(child, title);
                if (textChars > 0)
                    candidates.Add((child, score, textChars, density));

                // Recurse into containers to find nested content
                ScoreElements(child, title, candidates);
            }
        }
    }

    private (double Score, int TextChars, double Density) ScoreElement(IElement element, string? title)
    {
        var textChars = CountTextChars(element);
        if (textChars == 0) return (0, 0, 0);

        var lineCount = CountLines(element);
        if (lineCount == 0) lineCount = 1;

        var linkChars = CountLinkTextChars(element);
        var density = (double)textChars / lineCount;
        var linkRatio = (double)linkChars / Math.Max(textChars, 1);
        var noisePenalty = HasNoiseClass(element) ? 0.05 : 1.0;
        var titleBonus = title is not null ? 1.0 + ComputeTitleSimilarity(element, title) : 1.0;

        var score = density * (1.0 - linkRatio) * noisePenalty * titleBonus;
        return (score, textChars, density);
    }

    /// <summary>
    /// Recursively counts visible text characters in an element.
    /// Each Chinese character, letter, digit, and punctuation counts as 1.
    /// </summary>
    private static int CountTextChars(INode node)
    {
        return node switch
        {
            IText text => text.Data.Count(c => !char.IsWhiteSpace(c)),
            IElement element => element.ChildNodes.Sum(CountTextChars),
            _ => 0
        };
    }

    /// <summary>
    /// Counts text characters inside &lt;a&gt; tags (recursively).
    /// </summary>
    private static int CountLinkTextChars(INode node)
    {
        return node switch
        {
            IElement element when element.TagName.Equals("a", StringComparison.OrdinalIgnoreCase) =>
                element.ChildNodes.Sum(CountTextChars),
            IElement element =>
                element.ChildNodes.Sum(CountLinkTextChars),
            _ => 0
        };
    }

    /// <summary>
    /// Estimates the number of text "lines" in an element.
    /// Counts direct child block elements plus &lt;br&gt; tags plus
    /// 1 if there is inline text directly in this element.
    /// </summary>
    private static int CountLines(IElement element)
    {
        var lines = 0;
        var hasInlineText = false;

        foreach (var child in element.ChildNodes)
        {
            if (child is IElement childEl)
            {
                if (childEl.TagName.Equals("br", StringComparison.OrdinalIgnoreCase))
                {
                    lines++;
                }
                else if (LineTags.Contains(childEl.TagName))
                {
                    lines++;
                }
                else
                {
                    // Inline elements (span, a, strong, em, etc.) — their text counts as inline
                    if (HasTextContent(childEl))
                        hasInlineText = true;
                }
            }
            else if (child is IText text && text.Data.Any(c => !char.IsWhiteSpace(c)))
            {
                hasInlineText = true;
            }
        }

        if (hasInlineText) lines++;
        return Math.Max(lines, 1);
    }

    private static bool HasTextContent(INode node)
    {
        return node switch
        {
            IText text => text.Data.Any(c => !char.IsWhiteSpace(c)),
            IElement element => element.ChildNodes.Any(HasTextContent),
            _ => false
        };
    }

    /// <summary>
    /// Checks if the element's class or id contains noise keywords.
    /// </summary>
    private static bool HasNoiseClass(IElement element)
    {
        var classAttr = element.ClassName ?? "";
        var idAttr = element.Id ?? "";
        var combined = $"{classAttr} {idAttr}".ToLowerInvariant();

        foreach (var pattern in NoisePatterns)
        {
            if (combined.Contains(pattern))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Computes Jaccard similarity on character 2-gram sets
    /// between the element text and the page title.
    /// Returns 0.0 to 1.0 (higher = more similar).
    /// </summary>
    private static double ComputeTitleSimilarity(IElement element, string title)
    {
        var elementText = element.TextContent;
        if (string.IsNullOrWhiteSpace(elementText) || string.IsNullOrWhiteSpace(title))
            return 0.0;

        var elemGrams = GetBigrams(elementText);
        var titleGrams = GetBigrams(title);

        if (elemGrams.Count == 0 || titleGrams.Count == 0)
            return 0.0;

        var intersection = elemGrams.Intersect(titleGrams).Count();
        var union = elemGrams.Union(titleGrams).Count();

        return union > 0 ? (double)intersection / union : 0.0;
    }

    private static HashSet<string> GetBigrams(string text)
    {
        var grams = new HashSet<string>();
        for (var i = 0; i < text.Length - 1; i++)
        {
            var c1 = text[i];
            var c2 = text[i + 1];
            // Skip bigrams spanning whitespace
            if (char.IsWhiteSpace(c1) || char.IsWhiteSpace(c2))
                continue;
            grams.Add($"{c1}{c2}");
        }
        return grams;
    }
}
