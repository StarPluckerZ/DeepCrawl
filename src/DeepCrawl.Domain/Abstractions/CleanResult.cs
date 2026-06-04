using DeepCrawl.Domain.Models;

namespace DeepCrawl.Domain.Abstractions;

public class CleanResult
{
    public string Output { get; init; } = string.Empty;
    public string? CleanedHtml { get; init; }
    public bool AiCleaned { get; init; }
    public CrawlMetadata? Metadata { get; init; }
    public AiTokenUsage? TokenUsage { get; init; }
}
