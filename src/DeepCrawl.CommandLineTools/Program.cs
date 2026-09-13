using DeepCrawl.CommandLineTools.Commands;

namespace DeepCrawl.CommandLineTools;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var command = args[0].ToLowerInvariant();
        var remaining = args[1..];

        return command switch
        {
            "db" => HandleDb(remaining),
            "token" => HandleToken(remaining),
            "setup" => HandleSetup(remaining),
            _ => UnknownCommand(command)
        };
    }

    private static int HandleDb(string[] args)
    {
        if (args.Length == 0)
        {
            PrintDbUsage();
            return 1;
        }

        var sub = args[0].ToLowerInvariant();
        var opts = ParseOptions(args[1..]);

        return sub switch
        {
            "init" => DbCommand.Init(GetOpt(opts, "--connection-string")),
            _ => DbUsageError()
        };
    }

    private static int HandleToken(string[] args)
    {
        if (args.Length == 0)
        {
            PrintTokenUsage();
            return 1;
        }

        var sub = args[0].ToLowerInvariant();
        var opts = ParseOptions(args[1..]);

        return sub switch
        {
            "create" => TokenCommand.Create(
                GetOpt(opts, "--connection-string"),
                GetOpt(opts, "--name")),
            "list" => TokenCommand.List(
                GetOpt(opts, "--connection-string")),
            "revoke" => TokenCommand.Revoke(
                GetOpt(opts, "--connection-string"),
                GetOpt(opts, "--id")),
            _ => TokenUsageError()
        };
    }

    private static int HandleSetup(string[] args)
    {
        var opts = ParseOptions(args);
        return SetupCommand.Run(GetOpt(opts, "--connection-string"));
    }

    private static Dictionary<string, string?> ParseOptions(string[] args)
    {
        var dict = new Dictionary<string, string?>();
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--")) continue;
            var key = args[i];
            var value = (i + 1 < args.Length && !args[i + 1].StartsWith("--")) ? args[++i] : null;
            dict[key] = value;
        }
        return dict;
    }

    private static string? GetOpt(Dictionary<string, string?> opts, string key)
        => opts.TryGetValue(key, out var v) ? v : null;

    private static void PrintUsage()
    {
        Console.WriteLine("DeepCrawl.CommandLineTools — dev/admin utilities");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  db init          Sync database schema (all tables)");
        Console.WriteLine("  token create     Create an API token");
        Console.WriteLine("  token list       List API tokens");
        Console.WriteLine("  token revoke     Deactivate an API token");
        Console.WriteLine("  setup            One-shot: schema sync + initial API token");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet run -- setup");
        Console.WriteLine("  dotnet run -- token create --name laptop");
        Console.WriteLine("  dotnet run -- token revoke --id 3");
    }

    private static int DbUsageError() { PrintDbUsage(); return 1; }

    private static void PrintDbUsage()
    {
        Console.WriteLine("db <subcommand> [options]");
        Console.WriteLine();
        Console.WriteLine("Subcommands:");
        Console.WriteLine("  init    Sync all entity tables (crawl_records, crawl_statistics, api_tokens, domain_reputations)");
    }

    private static int TokenUsageError() { PrintTokenUsage(); return 1; }

    private static void PrintTokenUsage()
    {
        Console.WriteLine("token <subcommand> [options]");
        Console.WriteLine();
        Console.WriteLine("Subcommands:");
        Console.WriteLine("  create  [--name]          Create an API token (shown once)");
        Console.WriteLine("  list                      List tokens (id, name, prefix, active)");
        Console.WriteLine("  revoke  --id <id>         Deactivate a token by id");
        Console.WriteLine();
        Console.WriteLine("Common options:");
        Console.WriteLine("  --connection-string   PostgreSQL connection string");
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        return 1;
    }
}
