using AngleSharp.Html.Parser;
using DeepCrawl.Domain.Abstractions;

namespace DeepCrawl.Infrastructure.Cleaning;

public class ContentAnalyzer : IContentAnalyzer
{
    public int GetTextLength(string rawHtml)
    {
        var parser = new HtmlParser();
        var doc = parser.ParseDocument(rawHtml);
        if (doc.Body is null) return 0;

        var hasJsRequired = doc.Body.TextContent.Trim().StartsWith("You need to enable JavaScript")
            || doc.Body.TextContent.Contains("Please enable JavaScript");

        var roots = doc.Body.QuerySelectorAll("#root, #app, #__next, #__nuxt");
        var hasEmptyRoot = roots.Any(r => string.IsNullOrWhiteSpace(r.TextContent));

        var hasSpaMarker = doc.Body.InnerHtml.Contains("data-reactroot")
            || doc.Body.InnerHtml.Contains("__NEXT_DATA__")
            || doc.Body.InnerHtml.Contains("ng-version");

        var hasSemantic = doc.Body.QuerySelectorAll(
            "p, h1, h2, h3, h4, h5, h6, article, main, section, li, td, th").Length > 0;

        if (hasJsRequired || hasEmptyRoot)
            return 0;

        foreach (var el in doc.Body.QuerySelectorAll("script, style, noscript"))
            el.Remove();
        var text = doc.Body.TextContent.Trim();

        if (hasSpaMarker && text.Length < 500)
            return 0;
        if (!hasSemantic && text.Length < 500)
            return 0;

        return text.Length;
    }
}
