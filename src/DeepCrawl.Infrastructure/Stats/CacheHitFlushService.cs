using DeepCrawl.Domain.Abstractions;
using DeepCrawl.Domain.Entities;
using DeepCrawl.Domain.Models;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeepCrawl.Infrastructure.Stats;

/// <summary>
/// Background service that periodically flushes buffered cache-hit records
/// from a Redis List into the <c>crawl_statistics</c> table.
/// </summary>
/// <remarks>
/// Each cache hit in <see cref="Core.Services.CrawlPipeline"/> pushes the URL
/// into a Redis List (<c>RPUSH</c>). This service atomically pops the entire
/// queue (<c>LPOP … COUNT</c>), groups hits by URL, resolves each URL to its
/// latest <see cref="CrawlStatistic"/>, and increments
/// <see cref="CrawlStatistic.CacheHitCount"/> with a relative UPDATE so the
/// write is atomic and race-free.
/// </remarks>
public class CacheHitFlushService(
    IServiceScopeFactory scopeFactory)
    : BackgroundService
{
    private static readonly CacheKey QueueKey = new("Stats", "CacheHitQueue");
    private const int PopBatchSize = 10_000;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<CacheHitFlushService>>();
        logger.LogInformation("CacheHitFlushService starting (interval={Interval}s)", FlushInterval.TotalSeconds);
        var redis = scope.ServiceProvider.GetRequiredService<IRedisClient>();
        var recordRepo = scope.ServiceProvider.GetRequiredService<IBaseRepository<CrawlRecord>>();
        var statRepo = scope.ServiceProvider.GetRequiredService<IBaseRepository<CrawlStatistic>>();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(FlushInterval, stoppingToken);
                // Atomically pop up to PopBatchSize URLs from the queue
                var items = await redis.ListLeftPopAsync<string>(QueueKey, PopBatchSize, stoppingToken);
                if (items is not { Length: > 0 }) 
                    continue;

                logger.LogDebug("CacheHitFlush: popped {Count} cache-hit URLs", items.Length);

                // Group by URL and count hits per URL within this batch
                var urlCounts = items
                    .Where(u => u is not null)
                    .GroupBy(u => u!)
                    .ToDictionary(g => g.Key, g => g.Count());

                foreach (var (url, count) in urlCounts)
                {

                    try
                    {
                        // Find the latest AI-cleaned CrawlRecord for this URL
                        var record = await recordRepo
                            .Where(r => r.Url == url && r.CleanedMarkdown != null)
                            .OrderByDescending(r => r.Id)
                            .FirstAsync(stoppingToken);
                        if (record is null) continue;

                        // Find the latest CrawlStatistic for that record
                        var stat = await statRepo
                            .Where(s => s.CrawlRecordId == record.Id)
                            .OrderByDescending(s => s.Id)
                            .FirstAsync(stoppingToken);
                        if (stat is null) continue;

                        // Atomic relative update: clicks=clicks+1 pattern
                        await statRepo.UpdateDiy
                            .Set(s => s.CacheHitCount + count)
                            .Where(s => s.Id == stat.Id)
                            .ExecuteAffrowsAsync(stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "CacheHitFlush: failed to update stat for {Url}", url);
                    }
                }

                logger.LogDebug("CacheHitFlush: processed {UrlCount} unique URLs", urlCounts.Count);
            
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "CacheHitFlush failed");
            }
        }

        logger.LogInformation("CacheHitFlushService stopped");
    }


}
