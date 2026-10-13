using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using EnterpriseApim.Core;
using EnterpriseApim.Gateway;
using EnterpriseApim.Gateway.Auth;
using EnterpriseApim.Gateway.Configuration;
using EnterpriseApim.Gateway.Usage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

var gatewaySection = builder.Configuration.GetSection(GatewayOptions.Section);
builder.Services.Configure<GatewayOptions>(gatewaySection);
var gatewayOptions = gatewaySection.Get<GatewayOptions>() ?? new GatewayOptions();

// ---------------------------------------------------------------------------
// Data + background sync
// ---------------------------------------------------------------------------
builder.Services.AddApimDatabase(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<GatewayState>();
builder.Services.AddSingleton<ApiKeyCache>();
builder.Services.AddSingleton<KeyUsageTracker>();
builder.Services.AddScoped<ApiKeyValidator>();
builder.Services.AddSingleton<DbProxyConfigLoader>();
builder.Services.AddHostedService<ConfigSyncService>();
builder.Services.AddHostedService<KeyUsageFlushService>();

// ---------------------------------------------------------------------------
// Client authentication: the gateway only checks WHICH CLIENT is calling.
// User tokens (Authorization header) pass through untouched to the backends.
// ---------------------------------------------------------------------------
builder.Services.AddAuthentication(ApiKeyDefaults.Scheme)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyDefaults.Scheme, _ => { });

builder.Services.AddAuthorization(o =>
{
    // Every route without its own policy requires a valid client key,
    // including routes added to the database later.
    o.FallbackPolicy = new AuthorizationPolicyBuilder(ApiKeyDefaults.Scheme)
        .RequireAuthenticatedUser()
        .RequireClaim(ClientClaims.TenantId)
        .Build();
});
builder.Services.AddSingleton<IAuthorizationPolicyProvider, ScopePolicyProvider>();

// ---------------------------------------------------------------------------
// Rate limiting: per tenant for identified clients, per IP for everyone else.
// ---------------------------------------------------------------------------
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var tenantId = context.User.FindFirst(ClientClaims.TenantId)?.Value;
        if (tenantId is not null)
        {
            if (!int.TryParse(context.User.FindFirst(ClientClaims.RateLimit)?.Value, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var limit) || limit <= 0)
                return RateLimitPartition.GetNoLimiter($"tenant:{tenantId}");

            // The limit is part of the partition key so a changed limit takes effect immediately.
            return RateLimitPartition.GetFixedWindowLimiter($"tenant:{tenantId}:{limit}", _ =>
                new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(1) });
        }

        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter($"ip:{ip}", _ =>
            new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, gatewayOptions.AnonymousRateLimitPerMinute),
                Window = TimeSpan.FromMinutes(1)
            });
    });

    o.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        await ProblemResponses.WriteAsync(context.HttpContext.Response, StatusCodes.Status429TooManyRequests,
            "Rate limit exceeded", "Too many requests for this client. Retry after the time in the Retry-After header.",
            cancellationToken);
    };
});

// ---------------------------------------------------------------------------
// Trust X-Forwarded-For only from known load balancers.
// ---------------------------------------------------------------------------
if (gatewayOptions.KnownProxies.Length > 0)
{
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        foreach (var proxy in gatewayOptions.KnownProxies)
            o.KnownProxies.Add(IPAddress.Parse(proxy));
    });
}

// ---------------------------------------------------------------------------
// YARP: routes and clusters come from PostgreSQL via DbProxyConfigLoader.
// ---------------------------------------------------------------------------
builder.Services.AddReverseProxy()
    .LoadFromMemory([], [])
    .AddTransforms(context =>
    {
        context.AddRequestTransform(transform =>
        {
            var headers = transform.ProxyRequest.Headers;

            // Backends never see the client key, and clients can't spoof the tenant.
            headers.Remove(GatewayHeaders.ApiKey);
            headers.Remove(GatewayHeaders.TenantId);
            headers.Remove(GatewayHeaders.ClientKeyId);

            var client = transform.HttpContext.User;
            if (client.FindFirst(ClientClaims.TenantId)?.Value is { } tenantId)
                headers.TryAddWithoutValidation(GatewayHeaders.TenantId, tenantId);
            if (client.FindFirst(ClientClaims.KeyId)?.Value is { } keyId)
                headers.TryAddWithoutValidation(GatewayHeaders.ClientKeyId, keyId);

            // The Authorization header (the user's token) is forwarded unchanged.
            return ValueTask.CompletedTask;
        });
    });

var app = builder.Build();

if (gatewayOptions.KnownProxies.Length > 0)
    app.UseForwardedHeaders();

// Correlation ID: keep the caller's (if sane) or use the trace ID; echo it back and forward it.
app.Use((context, next) =>
{
    var incoming = context.Request.Headers[GatewayHeaders.CorrelationId].ToString();
    var correlationId = incoming.Length is > 0 and <= 100 ? incoming : context.TraceIdentifier;
    context.Request.Headers[GatewayHeaders.CorrelationId] = correlationId;
    context.Response.Headers[GatewayHeaders.CorrelationId] = new StringValues(correlationId);
    return next(context);
});

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }))
    .AllowAnonymous()
    .DisableRateLimiting();

app.MapGet("/health/ready", (GatewayState state) => state.Current is { } current
        ? Results.Ok(new { status = "ready", current.LoadedAt, current.Routes, current.Clusters, current.Skipped })
        : Results.Json(new { status = "loading" }, statusCode: StatusCodes.Status503ServiceUnavailable))
    .AllowAnonymous()
    .DisableRateLimiting();

app.MapReverseProxy();

app.Run();
