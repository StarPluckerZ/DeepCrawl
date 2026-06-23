using DeepCrawl.Domain.Abstractions;
using DeepCrawl.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace DeepCrawl.Infrastructure.Auth;

public class TokenValidator(IFreeSql fsql, ILogger<TokenValidator> logger) : ITokenValidator
{
    public async Task<bool> ValidateAsync(string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning("Empty or missing token");
            return false;
        }

        var valid = await fsql.Select<ApiToken>()
            .Where(t => t.Token == token && t.IsActive)
            .AnyAsync(ct);

        if (!valid)
            logger.LogWarning("Invalid or inactive token used");

        return valid;
    }
}
