using System.Security.Cryptography;
using DeepCrawl.Domain.Entities;
using FreeSql;

namespace DeepCrawl.CommandLineTools.Commands;

/// <summary>
/// API token management. Tokens are stored in plaintext (single-user system)
/// and shown in full only once at creation.
/// </summary>
internal static class TokenCommand
{
    /// <summary>Create a new API token and print it once.</summary>
    public static int Create(string? connectionString, string? name)
    {
        using var fsql = Db.Build(connectionString);

        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token = "sk-" + Convert.ToHexStringLower(tokenBytes);

        var inserted = fsql.Insert(new ApiToken
        {
            Name = name,
            Token = token,
            IsActive = true
        }).ExecuteInserted().First();

        Console.WriteLine("──────────────────────────────────────────────");
        Console.WriteLine($"  Token   : {name ?? "(unnamed)"} (id={inserted.Id})");
        Console.WriteLine("  API Key (save this — shown once):");
        Console.WriteLine($"  {token}");
        Console.WriteLine("──────────────────────────────────────────────");
        return 0;
    }

    /// <summary>List tokens with a truncated prefix (full key is never re-displayed).</summary>
    public static int List(string? connectionString)
    {
        using var fsql = Db.Build(connectionString);

        var tokens = fsql.Select<ApiToken>()
            .OrderBy(t => t.Id)
            .ToList();

        if (tokens.Count == 0)
        {
            Console.WriteLine("No API tokens. Run 'token create' or 'setup' first.");
            return 0;
        }

        Console.WriteLine($"{"Id",-5} {"Active",-7} {"Name",-20} Token");
        foreach (var t in tokens)
        {
            var prefix = t.Token.Length > 12 ? t.Token[..12] + "…" : t.Token;
            Console.WriteLine($"{t.Id,-5} {(t.IsActive ? "yes" : "no"),-7} {t.Name ?? "(unnamed)",-20} {prefix}");
        }
        return 0;
    }

    /// <summary>Deactivate a token by id (idempotent).</summary>
    public static int Revoke(string? connectionString, string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || !long.TryParse(id, out var tokenId))
        {
            Console.Error.WriteLine("Error: --id <numeric id> is required. Run 'token list' to find it.");
            return 1;
        }

        using var fsql = Db.Build(connectionString);

        var token = fsql.Select<ApiToken>().Where(t => t.Id == tokenId).First();
        if (token == null)
        {
            Console.Error.WriteLine($"Error: token id={tokenId} not found.");
            return 1;
        }

        if (!token.IsActive)
        {
            Console.WriteLine($"[exists] Token id={tokenId} ({token.Name ?? "unnamed"}) is already revoked");
            return 0;
        }

        fsql.Update<ApiToken>().Where(t => t.Id == tokenId).Set(t => t.IsActive, false).ExecuteAffrows();
        Console.WriteLine($"[revoked] Token id={tokenId} ({token.Name ?? "unnamed"})");
        return 0;
    }
}
