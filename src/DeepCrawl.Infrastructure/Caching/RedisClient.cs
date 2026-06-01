using DeepCrawl.Domain.Abstractions;
using DeepCrawl.Domain.Models;
using Medallion.Threading.Redis;
using StackExchange.Redis;

namespace DeepCrawl.Infrastructure.Caching;

public class RedisClient : IRedisClient
{
    private readonly IDatabase _db;
    private readonly RedisOptions _options;

    public IDatabase Database => _db;

    public RedisDistributedLock GetLock(string lockName) => new((RedisKey)$"{_options.Prefix}:{lockName}", _db);

    public RedisClient(IConnectionMultiplexer redis, RedisOptions options)
    {
        _db = redis.GetDatabase(options.Database);
        _options = options;
    }

    private string FullKey(CacheKey key) => $"{_options.Prefix}:{key.Prefix}:{key.Key}";
    private TimeSpan GetExpiry(CacheKey key) => key.Expiry ?? _options.DefaultTtl;

    public T? Get<T>(CacheKey key) => GetAsync<T>(key).GetAwaiter().GetResult();

    public async Task<T?> GetAsync<T>(CacheKey key, CancellationToken ct = default)
    {
        var val = await _db.StringGetAsync(FullKey(key)).WaitAsync(ct);
        return RedisSerializer.Deserialize<T>(val);
    }

    public void Set<T>(CacheKey key, T value) => SetAsync(key, value).GetAwaiter().GetResult();

    public async Task SetAsync<T>(CacheKey key, T value, CancellationToken ct = default)
    {
        var full = FullKey(key);
        var json = RedisSerializer.Serialize(value);
        await _db.StringSetAsync(full, json, GetExpiry(key)).WaitAsync(ct);
    }

    public bool Delete(CacheKey key) => DeleteAsync(key).GetAwaiter().GetResult();

    public async Task<bool> DeleteAsync(CacheKey key, CancellationToken ct = default)
    {
        return await _db.KeyDeleteAsync(FullKey(key)).WaitAsync(ct);
    }

    public bool Exists(CacheKey key) => _db.KeyExists(FullKey(key));

    public Task<bool> ExistsAsync(CacheKey key, CancellationToken ct = default)
    {
        return _db.KeyExistsAsync(FullKey(key)).WaitAsync(ct);
    }

    public T GetOrSet<T>(CacheKey key, Func<T> factory)
    {
        var cached = Get<T>(key);
        if (cached is not null) return cached;
        var value = factory();
        Set(key, value);
        return value;
    }

    public async Task<T> GetOrSetAsync<T>(CacheKey key, Func<CancellationToken, Task<T>> factory, CancellationToken ct = default)
    {
        var cached = await GetAsync<T>(key, ct);
        if (cached is not null) return cached;
        var value = await factory(ct);
        await SetAsync(key, value, ct);
        return value;
    }

    public T?[] GetMany<T>(CacheKey[] keys)
    {
        if (keys.Length == 0) return [];
        var redisKeys = keys.Select(k => (RedisKey)FullKey(k)).ToArray();
        var values = _db.StringGet(redisKeys);
        return values.Select(v => RedisSerializer.Deserialize<T>(v.ToString())).ToArray();
    }

    public async Task<T?[]> GetManyAsync<T>(CacheKey[] keys, CancellationToken ct = default)
    {
        if (keys.Length == 0) return [];
        var redisKeys = keys.Select(k => (RedisKey)FullKey(k)).ToArray();
        var values = await _db.StringGetAsync(redisKeys).WaitAsync(ct);
        return values.Select(v => RedisSerializer.Deserialize<T>(v.ToString())).ToArray();
    }

    public void SetMany<T>(KeyValuePair<CacheKey, T>[] entries)
    {
        if (entries.Length == 0) return;
        var batch = _db.CreateBatch();
        foreach (var (key, value) in entries)
            _ = batch.StringSetAsync(FullKey(key), RedisSerializer.Serialize(value), GetExpiry(key));
        batch.Execute();
    }

    public async Task SetManyAsync<T>(KeyValuePair<CacheKey, T>[] entries, CancellationToken ct = default)
    {
        if (entries.Length == 0) return;
        var batch = _db.CreateBatch();
        foreach (var (key, value) in entries)
            _ = batch.StringSetAsync(FullKey(key), RedisSerializer.Serialize(value), GetExpiry(key));
        batch.Execute();
    }

    public long DeleteByPrefix(string prefix) => DeleteByPrefixAsync(prefix).GetAwaiter().GetResult();

    public async Task<long> DeleteByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        var pattern = $"{prefix}*";
        var result = await _db.ScriptEvaluateAsync("""
            local cursor = '0'
            local count = 0
            repeat
                local r = redis.call('SCAN', cursor, 'MATCH', ARGV[1], 'COUNT', 100)
                cursor = r[1]
                for _,k in ipairs(r[2]) do
                    redis.call('DEL', k)
                    count = count + 1
                end
            until cursor == '0'
            return count
            """, values: new RedisValue[] { pattern }).WaitAsync(ct);
        return (long)result;
    }
}
