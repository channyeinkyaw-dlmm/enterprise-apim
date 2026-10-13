namespace EnterpriseApim.SampleBackend;

/// <summary>
/// Copy this into your real backends. The gateway proves WHICH CLIENT is calling
/// (X-Tenant-Id); your auth service proves WHICH USER (the token). This check makes sure
/// they belong together, so a user of tenant A can't act through tenant B's integration.
///
/// Requires: the user token carries a "tenant_id" claim set by your auth service at login,
/// and backends only accept traffic from the gateway (network rules or mTLS).
/// </summary>
public sealed class TenantConsistencyMiddleware(RequestDelegate next)
{
    public const string TenantHeader = "X-Tenant-Id";
    public const string TenantClaim = "tenant_id";

    public async Task InvokeAsync(HttpContext context)
    {
        var headerTenant = context.Request.Headers[TenantHeader].ToString();

        if (context.User.Identity?.IsAuthenticated == true)
        {
            var tokenTenant = context.User.FindFirst(TenantClaim)?.Value;
            if (string.IsNullOrEmpty(headerTenant) || !string.Equals(tokenTenant, headerTenant, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    title = "Tenant mismatch",
                    status = 403,
                    detail = "The signed-in user does not belong to the calling client."
                });
                return;
            }
        }

        await next(context);
    }
}
