using DeepCrawl.Domain.Models;

namespace DeepCrawl.Domain.Abstractions;

public interface ISearchProvider
{
    string ProviderName { get; }
    Task<List<SearchProviderResult>> SearchAsync(SearchProviderRequest request, CancellationToken ct = default);
}
