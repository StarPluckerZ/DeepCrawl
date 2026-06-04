using DeepCrawl.Domain.Models;

namespace DeepCrawl.Domain.Abstractions;

public interface IAIMarkdownCleaner
{
    Task<(string Text, AiTokenUsage? Usage)> CleanAsync(string rawMarkdown, CancellationToken ct = default);
}
