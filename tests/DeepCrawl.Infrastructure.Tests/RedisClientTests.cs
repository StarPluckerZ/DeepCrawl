using DeepCrawl.Domain.Abstractions;
using DeepCrawl.Domain.Models;
using DeepCrawl.Infrastructure.Caching;
using StackExchange.Redis;
using Xunit;

namespace DeepCrawl.Infrastructure.Tests;

public class RedisClientTests : IAsyncLifetime
{
    private readonly IRedisClient _client;
    private readonly ConnectionMultiplexer _redis;
    private readonly IServer _server;

    public RedisClientTests()
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

    private static CacheKey Key(string k) => new("test", k);

    [Fact]
    public void Set_And_Get_Value()
    {
        var key = Key("set-get");
        _client.Set(key, "hello");
        var result = _client.Get<string>(key);
        Assert.Equal("hello", result);
    }

    [Fact]
    public async Task SetAsync_And_GetAsync_Value()
    {
        var key = Key("set-get-async");
        await _client.SetAsync(key, "world");
        var result = await _client.GetAsync<string>(key);
        Assert.Equal("world", result);
    }

    [Fact]
    public void Get_Absent_Key_Returns_Null()
    {
        var key = Key("absent");
        var result = _client.Get<string>(key);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_Absent_Key_Returns_Null()
    {
        var key = Key("absent-async");
        var result = await _client.GetAsync<string>(key);
        Assert.Null(result);
    }

    [Fact]
    public void Set_Complex_Object()
    {
        var key = Key("complex");
        var obj = new TestDto { Id = 1, Name = "Alice" };
        _client.Set(key, obj);
        var result = _client.Get<TestDto>(key);
        Assert.NotNull(result);
        Assert.Equal(1, result.Id);
        Assert.Equal("Alice", result.Name);
    }

    [Fact]
    public async Task SetAsync_Complex_Object()
    {
        var key = Key("complex-async");
        var obj = new TestDto { Id = 2, Name = "Bob" };
        await _client.SetAsync(key, obj);
        var result = await _client.GetAsync<TestDto>(key);
        Assert.NotNull(result);
        Assert.Equal(2, result.Id);
        Assert.Equal("Bob", result.Name);
    }

    [Fact]
    public void Delete_Existing_Key()
    {
        var key = Key("del");
        _client.Set(key, "value");
        var deleted = _client.Delete(key);
        Assert.True(deleted);
        Assert.Null(_client.Get<string>(key));
    }

    [Fact]
    public async Task DeleteAsync_Existing_Key()
    {
        var key = Key("del-async");
        await _client.SetAsync(key, "value");
        var deleted = await _client.DeleteAsync(key);
        Assert.True(deleted);
        Assert.Null(await _client.GetAsync<string>(key));
    }

    [Fact]
    public void Delete_Absent_Key_Returns_False()
    {
        var key = Key("del-absent");
        var deleted = _client.Delete(key);
        Assert.False(deleted);
    }

    [Fact]
    public async Task DeleteAsync_Absent_Key_Returns_False()
    {
        var key = Key("del-absent-async");
        var deleted = await _client.DeleteAsync(key);
        Assert.False(deleted);
    }

    [Fact]
    public void Exists_Returns_True_For_Existing_Key()
    {
        var key = Key("exists");
        _client.Set(key, "val");
        Assert.True(_client.Exists(key));
    }

    [Fact]
    public void Exists_Returns_False_For_Absent_Key()
    {
        var key = Key("exists-absent");
        Assert.False(_client.Exists(key));
    }

    [Fact]
    public async Task ExistsAsync_Returns_True_For_Existing_Key()
    {
        var key = Key("exists-async");
        await _client.SetAsync(key, "val");
        Assert.True(await _client.ExistsAsync(key));
    }

    [Fact]
    public async Task ExistsAsync_Returns_False_For_Absent_Key()
    {
        var key = Key("exists-async-absent");
        Assert.False(await _client.ExistsAsync(key));
    }

    [Fact]
    public void GetOrSet_Calls_Factory_On_Cache_Miss()
    {
        var key = Key("getorset-miss");
        var factoryCalled = false;
        var result = _client.GetOrSet(key, () =>
        {
            factoryCalled = true;
            return "from-factory";
        });
        Assert.True(factoryCalled);
        Assert.Equal("from-factory", result);
        Assert.Equal("from-factory", _client.Get<string>(key));
    }

    [Fact]
    public void GetOrSet_Returns_Cached_Value_On_Hit()
    {
        var key = Key("getorset-hit");
        _client.Set(key, "cached");
        var factoryCalled = false;
        var result = _client.GetOrSet(key, () =>
        {
            factoryCalled = true;
            return "new";
        });
        Assert.False(factoryCalled);
        Assert.Equal("cached", result);
    }

    [Fact]
    public async Task GetOrSetAsync_Calls_Factory_On_Cache_Miss()
    {
        var key = Key("getorset-async-miss");
        var factoryCalled = false;
        var result = await _client.GetOrSetAsync(key, _ =>
        {
            factoryCalled = true;
            return Task.FromResult("from-async-factory");
        });
        Assert.True(factoryCalled);
        Assert.Equal("from-async-factory", result);
        Assert.Equal("from-async-factory", await _client.GetAsync<string>(key));
    }

    [Fact]
    public async Task GetOrSetAsync_Returns_Cached_Value_On_Hit()
    {
        var key = Key("getorset-async-hit");
        await _client.SetAsync(key, "cached-async");
        var factoryCalled = false;
        var result = await _client.GetOrSetAsync(key, _ =>
        {
            factoryCalled = true;
            return Task.FromResult("new");
        });
        Assert.False(factoryCalled);
        Assert.Equal("cached-async", result);
    }

    [Fact]
    public void GetMany_Returns_Values()
    {
        var keys = new[] { Key("many-1"), Key("many-2"), Key("many-3") };
        _client.Set(keys[0], "a");
        _client.Set(keys[1], "b");
        var results = _client.GetMany<string>(keys);
        Assert.Equal("a", results[0]);
        Assert.Equal("b", results[1]);
        Assert.Null(results[2]);
    }

    [Fact]
    public async Task GetManyAsync_Returns_Values()
    {
        var keys = new[] { Key("many-a-1"), Key("many-a-2") };
        await _client.SetAsync(keys[0], 42);
        await _client.SetAsync(keys[1], 99);
        var results = await _client.GetManyAsync<int>(keys);
        Assert.Equal(42, results[0]);
        Assert.Equal(99, results[1]);
    }

    [Fact]
    public void SetMany_Writes_Multiple()
    {
        var entries = new[]
        {
            KeyValuePair.Create(Key("setmany-1"), "x"),
            KeyValuePair.Create(Key("setmany-2"), "y")
        };
        _client.SetMany(entries);
        Assert.Equal("x", _client.Get<string>(Key("setmany-1")));
        Assert.Equal("y", _client.Get<string>(Key("setmany-2")));
    }

    [Fact]
    public async Task SetManyAsync_Writes_Multiple()
    {
        var entries = new[]
        {
            KeyValuePair.Create(Key("setmany-a-1"), "alpha"),
            KeyValuePair.Create(Key("setmany-a-2"), "beta")
        };
        await _client.SetManyAsync(entries);
        Assert.Equal("alpha", await _client.GetAsync<string>(Key("setmany-a-1")));
        Assert.Equal("beta", await _client.GetAsync<string>(Key("setmany-a-2")));
    }

    [Fact]
    public async Task DeleteByPrefix_Removes_All_Matching_Keys()
    {
        await _client.SetAsync(Key("prefix-a"), "1");
        await _client.SetAsync(Key("prefix-b"), "2");
        await _client.SetAsync(Key("prefix-c"), "3");
        await _client.SetAsync(Key("other"), "keep");

        Assert.Equal("1", await _client.GetAsync<string>(Key("prefix-a")));

        var deleted = await _client.DeleteByPrefixAsync("test:test:prefix-");
        Assert.Equal(3, deleted);

        Assert.Null(await _client.GetAsync<string>(Key("prefix-a")));
        Assert.Null(await _client.GetAsync<string>(Key("prefix-b")));
        Assert.Null(await _client.GetAsync<string>(Key("prefix-c")));
        Assert.Equal("keep", await _client.GetAsync<string>(Key("other")));
    }

    [Fact]
    public async Task GetOrSet_Uses_Key_Expiry()
    {
        var key = new CacheKey("test", "expiry-key", Expiry: TimeSpan.FromMilliseconds(500));
        await _client.GetOrSetAsync(key, _ => Task.FromResult("short-lived"));
        Assert.Equal("short-lived", await _client.GetAsync<string>(key));

        await Task.Delay(600);
        Assert.Null(await _client.GetAsync<string>(key));
    }

    [Fact]
    public async Task Set_Uses_Key_Expiry()
    {
        var key = new CacheKey("test", "set-expiry", Expiry: TimeSpan.FromMilliseconds(500));
        await _client.SetAsync(key, "expires");
        Assert.Equal("expires", await _client.GetAsync<string>(key));

        await Task.Delay(600);
        Assert.Null(await _client.GetAsync<string>(key));
    }

}

public class TestDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

