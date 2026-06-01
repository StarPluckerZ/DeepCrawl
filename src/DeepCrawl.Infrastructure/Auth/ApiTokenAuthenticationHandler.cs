using System.Security.Claims;
using System.Text.Encodings.Web;
using DeepCrawl.Domain.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DeepCrawl.Infrastructure.Auth;

public class ApiTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly ITokenValidator _tokenValidator;

    public ApiTokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ITokenValidator tokenValidator)
        : base(options, logger, encoder)
    {
        _tokenValidator = tokenValidator;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var auth = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(auth))
            return AuthenticateResult.Fail("Missing Authorization header");

        string token;
        if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            token = auth["Bearer ".Length..].Trim();
        else
            token = auth;

        if (string.IsNullOrWhiteSpace(token))
            return AuthenticateResult.Fail("Empty token");

        try
        {
            if (!await _tokenValidator.ValidateAsync(token, Context.RequestAborted))
                return AuthenticateResult.Fail("Invalid or inactive API token");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Token validation failed");
            return AuthenticateResult.Fail("Authentication service unavailable");
        }

        var claims = new[] { new Claim(ClaimTypes.Name, "api-user") };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}
