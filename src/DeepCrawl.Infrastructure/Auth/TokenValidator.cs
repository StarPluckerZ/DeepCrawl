using DeepCrawl.Domain.Abstractions;
using DeepCrawl.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace DeepCrawl.Infrastructure.Auth;

public class TokenValidator : ITokenValidator
{
    private readonly IFreeSql _fsql;
    private readonly ILogger<TokenValidator> _logger;

    public TokenValidator(IFreeSql fsql, ILogger<TokenValidator> logger)
    {
        _fsql = fsql;
        _logger = logger;
    }

    public async Task<bool> ValidateAsync(string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogWarning("Empty or missing token");
            return false;
        }

        var valid = await _fsql.Select<ApiToken>()
            .Where(t => t.Token == token && t.IsActive)
            .AnyAsync(ct);

        if (!valid)
            _logger.LogWarning("Invalid or inactive token used");

        return valid;
    }
}
