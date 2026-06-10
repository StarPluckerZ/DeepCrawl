using DeepCrawl.Domain.Abstractions;
using DeepCrawl.Domain.Entities;
using DeepCrawl.Domain.Models;
using FreeSql;
using Microsoft.Extensions.Logging;

namespace DeepCrawl.Infrastructure.Filtering;

public class DomainReputationService : IUrlFilter, IDomainReporter
{
    private readonly IBaseRepository<DomainReputation> _repo;
    private readonly IRedisClient _redis;
    private readonly ReputationOptions _options;
    private readonly ILogger<DomainReputationService> _logger;

    private static CacheKey BlockedKey(string domain, TimeSpan? ttl = null) => new("Reputation", $"Blocked:{domain}", ttl);

    public DomainReputationService(
        IBaseRepository<DomainReputation> repo,
        IRedisClient redis,
        ReputationOptions options,
        ILogger<DomainReputationService> logger)
    {
        _repo = repo;
        _redis = redis;
        _options = options;
        _logger = logger;
    }

    public Task LoadRulesAsync(CancellationToken ct = default) => Task.CompletedTask;

    public async Task<bool> IsBlockedAsync(string url, CancellationToken ct = default)
    {
        if (!_options.Enabled) return false;

        var domain = ExtractDomain(url);
        if (domain is null) return false;

        var cacheKey = BlockedKey(domain);
        if (await _redis.ExistsAsync(cacheKey, ct))
            return true;

        var record = await _repo
            .Where(r => r.Domain == domain && r.BlockedUntil > DateTime.Now)
            .FirstAsync(ct);

        if (record is null) return false;

        var remaining = record.BlockedUntil!.Value - DateTime.Now;
        if (remaining > TimeSpan.Zero)
        {
            await _redis.SetAsync(BlockedKey(domain, remaining), record.BlockedUntil.Value.ToString("O"), ct);
            return true;
        }

        return false;
    }

    public async Task RecordFailureAsync(string url, CancellationToken ct = default)
    {
        if (!_options.Enabled) return;

        var domain = ExtractDomain(url);
        if (domain is null) return;

        var record = await _repo.Where(r => r.Domain == domain).FirstAsync(ct)
                     ?? new DomainReputation { Domain = domain };

        record.ConsecutiveFailures++;
        record.TotalFailures++;
        record.LastFailureAt = DateTime.Now;
        record.UpdatedAt = DateTime.Now;

        var ttl = ComputeBlockDuration(record.ConsecutiveFailures);
        record.BlockedUntil = DateTime.Now + ttl;

        await _repo.InsertOrUpdateAsync(record, ct);

        await _redis.SetAsync(BlockedKey(domain, ttl), record.BlockedUntil.Value.ToString("O"), ct);

        _logger.LogInformation("Domain {Domain} blocked for {Minutes}min (failures={N})",
            domain, ttl.TotalMinutes, record.ConsecutiveFailures);
    }

    public async Task RecordSuccessAsync(string url, CancellationToken ct = default)
    {
        if (!_options.Enabled) return;

        var domain = ExtractDomain(url);
        if (domain is null) return;

        var record = await _repo.Where(r => r.Domain == domain).FirstAsync(ct);
        if (record is null) return;

        record.ConsecutiveFailures = 0;
        record.BlockedUntil = null;
        record.LastSuccessAt = DateTime.Now;
        record.UpdatedAt = DateTime.Now;
        await _repo.UpdateAsync(record, ct);

        await _redis.DeleteAsync(BlockedKey(domain), ct);
    }

    private TimeSpan ComputeBlockDuration(int consecutiveFailures)
    {
        var threshold = _options.BlockThreshold;
        var baseMinutes = _options.BaseBlockMinutes;
        double raw;

        if (consecutiveFailures <= threshold)
        {
            raw = baseMinutes * (1 + Math.Log2(Math.Max(consecutiveFailures, 0) + 1));
        }
        else
        {
            var atThreshold = baseMinutes * (1 + Math.Log2(threshold + 1));
            raw = atThreshold * Math.Pow(2, consecutiveFailures - threshold);
        }

        raw = Math.Min(raw, _options.MaxBlockMinutes);
        return TimeSpan.FromMinutes(raw);
    }

    private static string? ExtractDomain(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return uri.Host.ToLowerInvariant();
        if (Uri.TryCreate($"https://{url}", UriKind.Absolute, out uri))
            return uri.Host.ToLowerInvariant();
        return null;
    }
}
