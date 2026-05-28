namespace DeepCrawl.Domain.Abstractions;

public class CleanResult
{
    public string Output { get; init; } = string.Empty;
    public string? CleanedHtml { get; init; }
    public bool AiCleaned { get; init; }
    public CrawlMetadata? Metadata { get; init; }
}
