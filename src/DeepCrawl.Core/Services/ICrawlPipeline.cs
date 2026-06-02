using DeepCrawl.Core.Dtos;

namespace DeepCrawl.Core.Services;

public interface ICrawlPipeline
{
    Task<ScrapeResponse> ScrapeAsync(ScrapeRequest request, CancellationToken ct = default);
}
