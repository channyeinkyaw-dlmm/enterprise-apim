namespace EnterpriseApim.Core;

/// <summary>Claims the gateway attaches to an authenticated client (never a user).</summary>
public static class ClientClaims
{
    public const string TenantId = "tenant_id";
    public const string TenantName = "tenant_name";
    public const string KeyId = "key_id";
    public const string Scope = "scope";
    public const string RateLimit = "rate_limit";

    /// <summary>A key with this scope may call every scoped route.</summary>
    public const string AllScopes = "*";
}

/// <summary>Headers the gateway sets on forwarded requests. Client-supplied values are always stripped.</summary>
public static class GatewayHeaders
{
    public const string ApiKey = "X-Api-Key";
    public const string TenantId = "X-Tenant-Id";
    public const string ClientKeyId = "X-Client-Key-Id";
    public const string CorrelationId = "X-Correlation-Id";
}
