using DeepCrawl.Core.Dtos;

namespace DeepCrawl.Core.Services;

public interface ISearchService
{
    Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken ct = default);
}
