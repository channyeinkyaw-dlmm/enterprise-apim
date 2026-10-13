namespace EnterpriseApim.Core;

/// <summary>A B2B client organization that is allowed to call APIs through the gateway.</summary>
public sealed class Tenant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Requests per minute across all of the tenant's keys. Null means unlimited.</summary>
    public int? RateLimitPerMinute { get; set; }

    /// <summary>CIDR ranges (e.g. 203.0.113.0/24). Empty means any IP is allowed.</summary>
    public string[] AllowedIpRanges { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<ApiKey> ApiKeys { get; set; } = [];
}

/// <summary>An access key issued to a tenant. Only a hash of the secret is stored.</summary>
public sealed class ApiKey
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>Public, non-secret part of the key, used for lookup and logging.</summary>
    public string KeyId { get; set; } = "";
    public byte[] SecretHash { get; set; } = [];

    public string Name { get; set; } = "";

    /// <summary>Names from the scope catalog (<see cref="Scope"/>), or "*" for every scope.</summary>
    public string[] Scopes { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }

    public Tenant Tenant { get; set; } = null!;
}

/// <summary>A group of backend servers that routes forward to (a YARP cluster).</summary>
public sealed class GatewayCluster
{
    public string Id { get; set; } = "";
    public string? LoadBalancingPolicy { get; set; }
    public bool HealthCheckEnabled { get; set; }
    public string? HealthCheckPath { get; set; }
    public int? HealthCheckIntervalSeconds { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<ClusterDestination> Destinations { get; set; } = [];
}

public sealed class ClusterDestination
{
    public Guid Id { get; set; }
    public string ClusterId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
}

/// <summary>Matches incoming requests and sends them to a cluster (a YARP route).</summary>
public sealed class GatewayRoute
{
    public string Id { get; set; } = "";
    public string ClusterId { get; set; } = "";
    public string Path { get; set; } = "";

    /// <summary>HTTP methods to match. Empty means all methods.</summary>
    public string[] Methods { get; set; } = [];
    public int? Order { get; set; }

    /// <summary>When set, the client's key must carry this scope. References <see cref="Scope.Name"/>.</summary>
    public string? RequiredScope { get; set; }

    /// <summary>When true, no client key is required. Use sparingly.</summary>
    public bool AllowAnonymous { get; set; }

    /// <summary>YARP transforms as a JSON array, e.g. [{"PathRemovePrefix":"/api"}].</summary>
    public string? TransformsJson { get; set; }

    public bool IsEnabled { get; set; } = true;
    public string? Description { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// A permission that routes can require and keys can carry, such as "orders.read". The catalog of scopes
/// admins choose from; the name is what partners' keys carry, so it can't be renamed.
/// </summary>
public sealed class Scope
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A person who can sign in to the admin console. Only a salted password hash is stored.</summary>
public sealed class AdminUser
{
    public Guid Id { get; set; }

    /// <summary>Lowercase; usernames are case-insensitive.</summary>
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";

    /// <summary>Changes whenever the password changes or the user is disabled, which ends existing sessions.</summary>
    public string SecurityStamp { get; set; } = "";

    public bool IsActive { get; set; } = true;

    /// <summary>Set for new users and after a password reset; the console asks for a new password first.</summary>
    public bool MustChangePassword { get; set; }

    public int FailedSignInCount { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset? LastSignInAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
