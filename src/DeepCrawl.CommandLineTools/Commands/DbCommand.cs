namespace DeepCrawl.CommandLineTools.Commands;

/// <summary>
/// Database maintenance commands. Schema sync also runs implicitly on every
/// CLI invocation; this command exists for explicit init/verification.
/// </summary>
internal static class DbCommand
{
    public static int Init(string? connectionString)
    {
        using var fsql = Db.Build(connectionString);
        var tables = new[] { "crawl_records", "crawl_statistics", "api_tokens", "domain_reputations" };
        Console.WriteLine("[synced] " + string.Join(", ", tables));
        return 0;
    }
}
