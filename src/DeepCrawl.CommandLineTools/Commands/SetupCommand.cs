using DeepCrawl.Domain.Entities;
using FreeSql;

namespace DeepCrawl.CommandLineTools.Commands;

/// <summary>
/// One-shot bootstrap for a fresh deployment: schema sync + an initial API
/// token if none exists. Idempotent — safe to run repeatedly.
/// </summary>
internal static class SetupCommand
{
    public static int Run(string? connectionString)
    {
        using var fsql = Db.Build(connectionString);
        Console.WriteLine("[synced] Database schema (crawl_records, crawl_statistics, api_tokens, domain_reputations)");

        var tokenCount = fsql.Select<ApiToken>().Count();
        if (tokenCount > 0)
        {
            Console.WriteLine($"[exists] {tokenCount} API token(s) — run 'token create' to add more");
            return 0;
        }

        return TokenCommand.Create(connectionString, "default");
    }
}
