using DeepCrawl.Domain.Entities;
using FreeSql;
using FreeSql.DataAnnotations;
using Microsoft.Extensions.Configuration;

namespace DeepCrawl.CommandLineTools.Commands;

/// <summary>
/// Shared FreeSql factory — resolves the connection string and syncs the
/// entity schema on every run, so any command can run against a fresh database.
/// </summary>
internal static class Db
{
    public static IFreeSql Build(string? connectionString)
    {
        connectionString ??= new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true)
            .AddEnvironmentVariables()
            .Build()
            .GetConnectionString("PostgreSQL");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine("Error: PostgreSQL connection string required.");
            Environment.Exit(1);
        }

        var fsql = new FreeSqlBuilder()
            .UseConnectionString(DataType.PostgreSQL, connectionString)
            .UseAutoSyncStructure(true)
            .Build();

        var entityTypes = typeof(CrawlRecord).Assembly.GetTypes()
            .Where(t => Attribute.IsDefined(t, typeof(TableAttribute)))
            .ToArray();
        foreach (var type in entityTypes)
            fsql.CodeFirst.ConfigEntity(type, _ => { });

        fsql.CodeFirst.SyncStructure(entityTypes);

        return fsql;
    }
}
