namespace EnterpriseApim.AdminApi;

public sealed record TenantRequest(
    string? Name,
    string? Description,
    bool IsActive,
    int? RateLimitPerMinute,
    string[]? AllowedIpRanges);

public sealed record TenantResponse(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    int? RateLimitPerMinute,
    string[] AllowedIpRanges,
    int ActiveKeyCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateApiKeyRequest(string? Name, string[]? Scopes, DateTimeOffset? ExpiresAt);

/// <summary>The key's secret can't change; issue a new key for that.</summary>
public sealed record UpdateApiKeyRequest(string? Name, string[]? Scopes, DateTimeOffset? ExpiresAt);

public sealed record ApiKeyResponse(
    Guid Id,
    string KeyId,
    string Name,
    string[] Scopes,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? LastUsedAt);

/// <summary>Returned once, at creation. The plain key is never stored or shown again.</summary>
public sealed record CreatedApiKeyResponse(ApiKeyResponse Key, string PlainKey);

public sealed record DestinationDto(string? Name, string? Address);

public sealed record ClusterRequest(
    string? Id,
    string? LoadBalancingPolicy,
    bool HealthCheckEnabled,
    string? HealthCheckPath,
    int? HealthCheckIntervalSeconds,
    DestinationDto[]? Destinations);

public sealed record ClusterResponse(
    string Id,
    string? LoadBalancingPolicy,
    bool HealthCheckEnabled,
    string? HealthCheckPath,
    int? HealthCheckIntervalSeconds,
    DestinationDto[] Destinations,
    int RouteCount,
    DateTimeOffset UpdatedAt);

public sealed record RouteRequest(
    string? Id,
    string? ClusterId,
    string? Path,
    string[]? Methods,
    int? Order,
    string? RequiredScope,
    bool AllowAnonymous,
    string? TransformsJson,
    bool IsEnabled,
    string? Description);

public sealed record RouteResponse(
    string Id,
    string ClusterId,
    string Path,
    string[] Methods,
    int? Order,
    string? RequiredScope,
    bool AllowAnonymous,
    string? TransformsJson,
    bool IsEnabled,
    string? Description,
    DateTimeOffset UpdatedAt);

public sealed record SignInRequest(string? Username, string? Password);

public sealed record CurrentUserResponse(Guid Id, string Username, bool MustChangePassword);

public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

public sealed record CreateAdminUserRequest(string? Username, string? Password);

public sealed record ResetPasswordRequest(string? Password);

public sealed record AdminUserResponse(
    Guid Id,
    string Username,
    bool IsActive,
    bool MustChangePassword,
    bool IsLocked,
    DateTimeOffset? LastSignInAt,
    DateTimeOffset CreatedAt);

public sealed record ScopeRequest(string? Name, string? Description);

public sealed record ScopeResponse(
    string Name,
    string? Description,
    int RouteCount,
    int ActiveKeyCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
