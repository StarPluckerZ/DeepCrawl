using DeepCrawl.Domain.Entities;

namespace DeepCrawl.Domain.Abstractions;

public interface ICrawlRepository
{
    Task<CrawlRecord?> GetByUrlAsync(string url, CancellationToken ct = default);
    Task<CrawlRecord?> GetByUrlAndHashAsync(string url, string htmlHash, string contextHash, CancellationToken ct = default);
    Task UpsertAsync(CrawlRecord record, CancellationToken ct = default);
    Task<CrawlRecord?> GetByIdAsync(long id, CancellationToken ct = default);
}
