namespace DeepCrawl.Domain.Models;

public record SearchProviderRequest
{
    public string Query { get; init; } = null!;
    public int Count { get; init; } = 10;
    public string? Freshness { get; init; }
    public bool Summary { get; init; }
    public string? Include { get; init; }
    public string? Exclude { get; init; }
}
