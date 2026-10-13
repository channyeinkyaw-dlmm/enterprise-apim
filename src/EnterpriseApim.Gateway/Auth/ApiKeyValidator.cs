using System.Net;
using System.Security.Cryptography;
using EnterpriseApim.Core;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseApim.Gateway.Auth;

public sealed record ApiKeyValidationResult(CachedApiKey? Key, string? Error);

public sealed class ApiKeyValidator(ApimDbContext db, ApiKeyCache cache, TimeProvider time)
{
    public async Task<ApiKeyValidationResult> ValidateAsync(string presentedKey, IPAddress? remoteIp,
        CancellationToken cancellationToken)
    {
        if (!ApiKeyFormat.TryParse(presentedKey, out var keyId, out var secret))
            return Fail("The API key is malformed.");

        var key = await cache.GetOrLoadAsync(keyId, () => LoadAsync(keyId, cancellationToken));

        // Unknown key ID and wrong secret get the same answer.
        if (key is null || !CryptographicOperations.FixedTimeEquals(key.SecretHash, ApiKeyFormat.HashSecret(secret)))
            return Fail("The API key is not valid.");

        if (key.RevokedAt is not null)
            return Fail("The API key has been revoked.");

        if (key.ExpiresAt is { } expiresAt && expiresAt <= time.GetUtcNow())
            return Fail("The API key has expired.");

        if (!key.TenantActive)
            return Fail("The client account is disabled.");

        if (key.HasIpRestriction && !IsAllowedIp(key, remoteIp))
            return Fail("Requests from this IP address are not allowed for this client.");

        return new ApiKeyValidationResult(key, null);
    }

    private static bool IsAllowedIp(CachedApiKey key, IPAddress? remoteIp)
    {
        if (remoteIp is null) return false;
        if (remoteIp.IsIPv4MappedToIPv6) remoteIp = remoteIp.MapToIPv4();
        return key.AllowedNetworks.Any(network => network.Contains(remoteIp));
    }

    private async Task<CachedApiKey?> LoadAsync(string keyId, CancellationToken cancellationToken)
    {
        var row = await db.ApiKeys.AsNoTracking()
            .Where(k => k.KeyId == keyId)
            .Select(k => new
            {
                k.Id,
                k.KeyId,
                k.TenantId,
                TenantName = k.Tenant.Name,
                k.SecretHash,
                k.Scopes,
                k.ExpiresAt,
                k.RevokedAt,
                TenantActive = k.Tenant.IsActive,
                k.Tenant.RateLimitPerMinute,
                k.Tenant.AllowedIpRanges
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null) return null;

        var networks = new List<System.Net.IPNetwork>();
        foreach (var range in row.AllowedIpRanges)
        {
            if (IpRanges.TryParse(range, out var network)) networks.Add(network);
        }

        return new CachedApiKey(
            row.Id, row.KeyId, row.TenantId, row.TenantName, row.SecretHash, row.Scopes,
            row.ExpiresAt, row.RevokedAt, row.TenantActive, row.RateLimitPerMinute,
            // Fail closed: if ranges are configured but none parse, nothing is allowed.
            HasIpRestriction: row.AllowedIpRanges.Length > 0,
            AllowedNetworks: networks.ToArray());
    }

    private static ApiKeyValidationResult Fail(string error) => new(null, error);
}
