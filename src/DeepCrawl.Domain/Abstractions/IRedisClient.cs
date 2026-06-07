using DeepCrawl.Domain.Models;
using Medallion.Threading.Redis;
using StackExchange.Redis;

namespace DeepCrawl.Domain.Abstractions;

public interface IRedisClient
{
    IDatabase Database { get; }

    RedisDistributedLock GetLock(string lockName);

    Task<T?> GetAsync<T>(CacheKey key, CancellationToken ct = default);
    T? Get<T>(CacheKey key);

    Task SetAsync<T>(CacheKey key, T value, CancellationToken ct = default);
    void Set<T>(CacheKey key, T value);

    Task<bool> DeleteAsync(CacheKey key, CancellationToken ct = default);
    bool Delete(CacheKey key);

    Task<bool> ExistsAsync(CacheKey key, CancellationToken ct = default);
    bool Exists(CacheKey key);

    Task<T> GetOrSetAsync<T>(CacheKey key, Func<CancellationToken, Task<T>> factory, CancellationToken ct = default);
    T GetOrSet<T>(CacheKey key, Func<T> factory);

    Task<T?[]> GetManyAsync<T>(CacheKey[] keys, CancellationToken ct = default);
    T?[] GetMany<T>(CacheKey[] keys);

    Task SetManyAsync<T>(KeyValuePair<CacheKey, T>[] entries, CancellationToken ct = default);
    void SetMany<T>(KeyValuePair<CacheKey, T>[] entries);

    Task<long> DeleteByPrefixAsync(string prefix, CancellationToken ct = default);
    long DeleteByPrefix(string prefix);

    Task<bool> SetContainsAsync(CacheKey key, string member, CancellationToken ct = default);
    Task SetAddAsync(CacheKey key, string[] members, CancellationToken ct = default);

    Task<HashEntry[]> HashGetAllAsync(CacheKey key, CancellationToken ct = default);
    Task HashIncrementAsync(CacheKey key, string field, long value = 1, CancellationToken ct = default);
    Task HashSetAsync(CacheKey key, HashEntry[] entries, CancellationToken ct = default);

    Task KeyExpireAsync(CacheKey key, TimeSpan expiry, CancellationToken ct = default);
    Task RenameKeyAsync(CacheKey key, CacheKey newKey, CancellationToken ct = default);
}
