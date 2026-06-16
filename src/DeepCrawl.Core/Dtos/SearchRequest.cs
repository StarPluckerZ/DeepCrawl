namespace DeepCrawl.Core.Dtos;

public record SearchRequest
{
    public string Query { get; init; } = null!;

    public int? Limit { get; init; } = 10;

    public List<string>? Sources { get; init; }

    public string? Tbs { get; init; }

    public string? Location { get; init; }

    public string? Country { get; init; }

    public List<string>? IncludeDomains { get; init; }

    public List<string>? ExcludeDomains { get; init; }

    public bool Summary { get; init; }

    public int? Timeout { get; init; } = 60000;
}
