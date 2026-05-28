namespace DeepCrawl.Domain.Abstractions;

public interface IAIMarkdownCleaner
{
    Task<string> CleanAsync(string rawMarkdown, CancellationToken ct = default);
}
