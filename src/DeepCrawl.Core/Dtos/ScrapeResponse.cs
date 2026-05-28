using DeepCrawl.Domain.Abstractions;

namespace DeepCrawl.Core.Dtos;

public record ScrapeResponse
{
    public bool Success { get; init; }
    public ScrapeData? Data { get; init; }
    public string? Error { get; init; }
}

public record ScrapeData
{
    public string? Markdown { get; init; }
    public string? Html { get; init; }
    public CrawlMetadata? Metadata { get; init; }
}
