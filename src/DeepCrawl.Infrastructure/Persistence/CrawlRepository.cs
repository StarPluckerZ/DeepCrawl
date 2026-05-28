using DeepCrawl.Domain.Abstractions;
using DeepCrawl.Domain.Entities;
using DeepCrawl.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace DeepCrawl.Infrastructure.Persistence;

public class CrawlRepository : ICrawlRepository
{
    private readonly IFreeSql _fsql;
    private readonly ILogger<CrawlRepository> _logger;

    public CrawlRepository(IFreeSql fsql, ILogger<CrawlRepository> logger)
    {
        _fsql = fsql;
        _logger = logger;
    }

    public Task<CrawlRecord?> GetByUrlAsync(string url, CancellationToken ct = default)
    {
        return _fsql.Select<CrawlRecord>()
            .Where(r => r.Url == url)
            .OrderByDescending(r => r.CreatedAt)
            .ToOneAsync(ct)!;
    }

    public Task<CrawlRecord?> GetByUrlAndHashAsync(string url, string htmlHash, string contextHash, CancellationToken ct = default)
    {
        return _fsql.Select<CrawlRecord>()
            .Where(r => r.Url == url && r.HtmlHash == htmlHash && r.ContextHash == contextHash && r.Status == nameof(CrawlStatus.Completed))
            .ToOneAsync(ct)!;
    }

    public async Task UpsertAsync(CrawlRecord record, CancellationToken ct = default)
    {
        var existing = await _fsql.Select<CrawlRecord>()
            .Where(r => r.Url == record.Url && r.ContextHash == record.ContextHash)
            .ToOneAsync(ct);

        if (existing is not null)
        {
            record.Id = existing.Id;
            await _fsql.Update<CrawlRecord>().SetSource(record).ExecuteAffrowsAsync(ct);
            _logger.LogInformation("Updated CrawlRecord {Id} for {Url}", record.Id, record.Url);
        }
        else
        {
            await _fsql.Insert(record).ExecuteAffrowsAsync(ct);
            _logger.LogInformation("Inserted CrawlRecord {Id} for {Url}", record.Id, record.Url);
        }
    }

    public Task<CrawlRecord?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        return _fsql.Select<CrawlRecord>().Where(r => r.Id == id).ToOneAsync(ct)!;
    }
}
