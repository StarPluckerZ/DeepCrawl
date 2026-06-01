namespace DeepCrawl.Domain.Models;

public readonly record struct CacheKey(string Prefix, string Key, TimeSpan? Expiry = null);