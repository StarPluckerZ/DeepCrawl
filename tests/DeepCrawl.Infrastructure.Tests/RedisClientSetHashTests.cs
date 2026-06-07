using DeepCrawl.Domain.Abstractions;
using DeepCrawl.Domain.Models;
using DeepCrawl.Infrastructure.Caching;
using StackExchange.Redis;
using Xunit;

namespace DeepCrawl.Infrastructure.Tests;

public class RedisClientSetHashTests : IAsyncLifetime
{
    private readonly IRedisClient _client;
    private readonly ConnectionMultiplexer _redis;
    private readonly IServer _server;

    public RedisClientSetHashTests()
    {
        var options = new RedisOptions
        {
            Host = "localhost",
            Port = int.Parse(Environment.GetEnvironmentVariable("TEST_REDIS_PORT") ?? "6379"),
            Password = "",
            Database = 0,
            Prefix = "test",
            DefaultTtl = TimeSpan.FromSeconds(5)
        };
        _redis = ConnectionMultiplexer.Connect($"{options.Host}:{options.Port},allowAdmin=true");
        _server = _redis.GetServer(options.Host, options.Port);
        _client = new RedisClient(_redis, options);
    }

    public async Task InitializeAsync()
    {
        await _server.FlushDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        _redis.Dispose();
        return Task.CompletedTask;
    }

    private static CacheKey Key(string k, TimeSpan? expiry = null) => new("test", k, expiry);

    // ── SET operations ──

    [Fact]
    public async Task SetContainsAsync_Returns_True_When_Member_Exists()
    {
        var key = Key("set-contains-true");
        await _client.SetAddAsync(key, ["a", "b", "c"]);
        Assert.True(await _client.SetContainsAsync(key, "b"));
    }

    [Fact]
    public async Task SetContainsAsync_Returns_False_When_Member_Absent()
    {
        var key = Key("set-contains-false");
        await _client.SetAddAsync(key, ["a", "b"]);
        Assert.False(await _client.SetContainsAsync(key, "z"));
    }

    [Fact]
    public async Task SetContainsAsync_Returns_False_When_Set_Empty()
    {
        var key = Key("set-contains-empty");
        Assert.False(await _client.SetContainsAsync(key, "anything"));
    }

    [Fact]
    public async Task SetAddAsync_Stores_And_Deletes_Correctly()
    {
        var key = Key("set-add-delete");
        await _client.SetAddAsync(key, ["x", "y", "z"]);
        Assert.True(await _client.SetContainsAsync(key, "x"));
        Assert.True(await _client.SetContainsAsync(key, "y"));
        Assert.True(await _client.SetContainsAsync(key, "z"));
        Assert.False(await _client.SetContainsAsync(key, "w"));

        await _client.DeleteAsync(key);
        Assert.False(await _client.SetContainsAsync(key, "x"));
    }

    // ── HASH operations ──

    [Fact]
    public async Task HashSetAsync_And_HashGetAllAsync_Roundtrip()
    {
        var key = Key("hash-roundtrip");
        var entries = new HashEntry[]
        {
            new("name", "Alice"),
            new("age", 30),
            new("active", true)
        };
        await _client.HashSetAsync(key, entries);

        var result = await _client.HashGetAllAsync(key);
        Assert.Equal(3, result.Length);

        var dict = result.ToDictionary(e => e.Name.ToString(), e => e.Value.ToString());
        Assert.Equal("Alice", dict["name"]);
        Assert.Equal("30", dict["age"]);
    }

    [Fact]
    public async Task HashGetAllAsync_Returns_Empty_For_Missing_Key()
    {
        var key = Key("hash-missing");
        var result = await _client.HashGetAllAsync(key);
        Assert.Empty(result);
    }

    [Fact]
    public async Task HashIncrementAsync_Creates_And_Increments()
    {
        var key = Key("hash-incr");
        await _client.HashIncrementAsync(key, "counter");
        await _client.HashIncrementAsync(key, "counter");
        await _client.HashIncrementAsync(key, "counter", 3);

        var result = await _client.HashGetAllAsync(key);
        var dict = result.ToDictionary(e => e.Name.ToString(), e => e.Value.ToString());
        Assert.Equal("5", dict["counter"]);
    }

    [Fact]
    public async Task HashIncrementAsync_Multiple_Fields()
    {
        var key = Key("hash-incr-multi");
        await _client.HashIncrementAsync(key, "attempts");
        await _client.HashIncrementAsync(key, "failures");
        await _client.HashIncrementAsync(key, "attempts", 4);

        var result = await _client.HashGetAllAsync(key);
        var dict = result.ToDictionary(e => e.Name.ToString(), e => (int)e.Value);
        Assert.Equal(5, dict["attempts"]);
        Assert.Equal(1, dict["failures"]);
    }

    [Fact]
    public async Task HashSetAsync_Overwrites_Existing_Fields()
    {
        var key = Key("hash-overwrite");
        await _client.HashSetAsync(key, [new HashEntry("status", "pending")]);
        await _client.HashSetAsync(key, [new HashEntry("status", "blocked")]);

        var result = await _client.HashGetAllAsync(key);
        Assert.Single(result);
        Assert.Equal("blocked", result[0].Value.ToString());
    }

    // ── Key expiry ──

    [Fact]
    public async Task KeyExpireAsync_Sets_TTL()
    {
        var key = Key("expire-ttl", TimeSpan.FromMinutes(10));
        await _client.SetAddAsync(key, ["item"]);
        await _client.KeyExpireAsync(key, TimeSpan.FromMilliseconds(300));

        Assert.True(await _client.SetContainsAsync(key, "item"));

        await Task.Delay(500);
        Assert.False(await _client.SetContainsAsync(key, "item"));
    }

    [Fact]
    public async Task KeyExpireAsync_Works_With_Hash_Keys()
    {
        var key = Key("expire-hash", TimeSpan.FromMinutes(10));
        await _client.HashSetAsync(key, [new HashEntry("x", 1)]);
        await _client.KeyExpireAsync(key, TimeSpan.FromMilliseconds(300));

        Assert.NotEmpty(await _client.HashGetAllAsync(key));

        await Task.Delay(500);
        Assert.Empty(await _client.HashGetAllAsync(key));
    }
}
