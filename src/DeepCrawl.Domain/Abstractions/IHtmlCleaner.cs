namespace DeepCrawl.Domain.Abstractions;

public interface IHtmlCleaner
{
    Task<string> CleanAsync(string rawHtml);
}
