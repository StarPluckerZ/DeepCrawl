using DeepCrawl.Domain.Enums;

namespace DeepCrawl.Core.Dtos;

public record CrawlResponse
{
    public string Url { get; init; } = null!;
    public string? Markdown { get; init; }
    public string Status { get; init; } = CrawlStatus.Pending.ToString();
    public bool FromCache { get; init; }
    public bool AiCleaned { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}
