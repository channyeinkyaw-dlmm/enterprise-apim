using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using EnterpriseApim.Core;
using EnterpriseApim.Gateway.Usage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace EnterpriseApim.Gateway.Auth;

/// <summary>
/// Identifies the calling B2B client from the X-Api-Key header. User authentication is not
/// done here: the user's Authorization header is forwarded untouched to the backends.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApiKeyValidator validator,
    KeyUsageTracker usage,
    TimeProvider time)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var presented = Request.Headers[GatewayHeaders.ApiKey].ToString();
        if (string.IsNullOrWhiteSpace(presented))
            return AuthenticateResult.NoResult();

        var result = await validator.ValidateAsync(presented, Context.Connection.RemoteIpAddress, Context.RequestAborted);
        if (result.Key is not { } key)
        {
            Logger.LogInformation("Rejected client request to {Path}: {Reason}", Request.Path, result.Error);
            return AuthenticateResult.Fail(result.Error ?? "The API key is not valid.");
        }

        usage.Record(key.ApiKeyId, time.GetUtcNow());

        var claims = new List<Claim>
        {
            new(ClientClaims.TenantId, key.TenantId.ToString()),
            new(ClientClaims.TenantName, key.TenantName),
            new(ClientClaims.KeyId, key.KeyId)
        };
        if (key.RateLimitPerMinute is { } limit)
            claims.Add(new Claim(ClientClaims.RateLimit, limit.ToString(CultureInfo.InvariantCulture)));
        claims.AddRange(key.Scopes.Select(scope => new Claim(ClientClaims.Scope, scope)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, ApiKeyDefaults.Scheme));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, ApiKeyDefaults.Scheme));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var result = await HandleAuthenticateOnceSafeAsync();
        var detail = result.Failure?.Message
            ?? $"This API requires a client key. Send it in the {GatewayHeaders.ApiKey} header.";

        Response.Headers.WWWAuthenticate = $"ApiKey header=\"{GatewayHeaders.ApiKey}\"";
        await ProblemResponses.WriteAsync(Response, StatusCodes.Status401Unauthorized, "Client not authenticated", detail);
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        ProblemResponses.WriteAsync(Response, StatusCodes.Status403Forbidden, "Client not allowed",
            "This API key does not have the scope required for this route.");
}
