namespace DeepCrawl.Domain.Abstractions;

public interface IContentAnalyzer
{
    int GetTextLength(string rawHtml);
}
