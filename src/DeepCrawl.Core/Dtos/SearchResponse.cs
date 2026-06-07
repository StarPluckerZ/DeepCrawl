namespace DeepCrawl.Core.Dtos;

public record SearchResponse
{
    public bool Success { get; init; }
    public SearchData? Data { get; init; }
    public string? Error { get; init; }
    public string? Warning { get; init; }
}

public record SearchData
{
    public List<SearchResult>? Web { get; init; }
}

public record SearchResult(string Title, string Description, string Url);
