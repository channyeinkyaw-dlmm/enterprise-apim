using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace EnterpriseApim.Gateway.Auth;

public sealed record CachedApiKey(
    Guid ApiKeyId,
    string KeyId,
    Guid TenantId,
    string TenantName,
    byte[] SecretHash,
    string[] Scopes,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    bool TenantActive,
    int? RateLimitPerMinute,
    bool HasIpRestriction,
    System.Net.IPNetwork[] AllowedNetworks);

/// <summary>
/// Short-lived cache of key lookups so the database is not queried on every request.
/// Unknown key IDs are cached too (as null) to blunt floods of made-up keys.
/// </summary>
public sealed class ApiKeyCache(IOptions<GatewayOptions> options) : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 100_000 });
    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(Math.Max(1, options.Value.KeyCacheSeconds));

    public async Task<CachedApiKey?> GetOrLoadAsync(string keyId, Func<Task<CachedApiKey?>> load)
    {
        if (_cache.TryGetValue(keyId, out CachedApiKey? cached)) return cached;

        var loaded = await load();
        _cache.Set(keyId, loaded, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _ttl,
            Size = 1
        });
        return loaded;
    }

    public void Remove(string keyId) => _cache.Remove(keyId);

    public void Clear() => _cache.Clear();

    public void Dispose() => _cache.Dispose();
}
