using DeepCrawl.Domain.Abstractions;
using DeepCrawl.Domain.Entities;
using DeepCrawl.Infrastructure.Caching;
using FreeSql;
using FreeSql.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace DeepCrawl.Infrastructure;

public static partial class ServiceCollectionExtensions
{
    /// <summary>Redis cache + PostgreSQL (FreeSql, code-first schema sync).</summary>
    private static IServiceCollection AddDataStores(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Redis
        var redisOptions = configuration.GetSection("Redis").Get<RedisOptions>() ?? new RedisOptions();
        services.AddSingleton(redisOptions);
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect($"{redisOptions.Host}:{redisOptions.Port},password={redisOptions.Password}"));
        services.AddSingleton<IRedisClient, RedisClient>();

        // PostgreSQL + FreeSql
        var fsql = new FreeSqlBuilder()
            .UseConnectionString(DataType.PostgreSQL, configuration.GetConnectionString("PostgreSQL"))
            .UseAutoSyncStructure(true)
            .Build();

        var entityTypes = typeof(CrawlRecord).Assembly.GetTypes()
            .Where(t => Attribute.IsDefined(t, typeof(TableAttribute)))
            .ToArray();
        foreach (var type in entityTypes)
            fsql.CodeFirst.ConfigEntity(type, _ => { });

        // Force sync all discovered tables at startup (lazy AutoSyncStructure
        // only syncs a table on first access, so CrawlStatistic would never
        // be created unless AI cleaning actually ran).
        fsql.CodeFirst.SyncStructure(entityTypes);

        services.AddSingleton<IFreeSql>(fsql);
        services.AddFreeRepository();

        return services;
    }
}
