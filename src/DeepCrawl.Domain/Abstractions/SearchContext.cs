using DeepCrawl.Domain.Models;

namespace DeepCrawl.Domain.Abstractions;

public class SearchContext
{
    public string Query { get; init; } = null!;
    public List<SearchProviderResult>? RawResults { get; set; }
}
