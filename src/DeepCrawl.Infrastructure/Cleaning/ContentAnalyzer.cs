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
        foreach (var el in doc.Body.QuerySelectorAll("script, style"))
            el.Remove();
        return doc.Body.TextContent.Trim().Length;
    }
}
