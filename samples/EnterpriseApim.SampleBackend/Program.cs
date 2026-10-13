using EnterpriseApim.SampleBackend;

// A stand-in backend that echoes what it receives from the gateway, so you can see
// which headers arrive (tenant ID, client key ID, the user's Authorization header)
// and confirm the raw API key never reaches the backend.
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// In a real backend: app.UseAuthentication(); (validating the user's token) then this.
app.UseMiddleware<TenantConsistencyMiddleware>();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/public/status", () => Results.Ok(new { status = "operational", checkedAt = DateTimeOffset.UtcNow }));

app.Map("/{**path}", (HttpContext context) =>
{
    var headers = context.Request.Headers;
    return Results.Ok(new
    {
        receivedBy = "sample-backend",
        method = context.Request.Method,
        path = context.Request.Path.Value,
        query = context.Request.QueryString.Value,
        tenantId = headers["X-Tenant-Id"].ToString(),
        clientKeyId = headers["X-Client-Key-Id"].ToString(),
        correlationId = headers["X-Correlation-Id"].ToString(),
        forwardedFor = headers["X-Forwarded-For"].ToString(),
        userAuthorizationForwarded = headers.ContainsKey("Authorization"),
        rawApiKeyLeaked = headers.ContainsKey("X-Api-Key")
    });
});

app.Run();
