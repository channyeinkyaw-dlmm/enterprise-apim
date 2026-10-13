namespace EnterpriseApim.Core;

/// <summary>
/// The admin API publishes changes with PostgreSQL NOTIFY; every gateway instance LISTENs
/// and reloads routes or evicts cached keys immediately.
/// </summary>
public static class ChangeNotifications
{
    public const string Channel = "apim_changes";

    /// <summary>Routes or clusters changed.</summary>
    public const string Config = "config";

    /// <summary>Reload everything (used after a listener reconnects).</summary>
    public const string Resync = "resync";

    private const string KeyPrefix = "key:";
    private const string TenantPrefix = "tenant:";

    public static string ForKey(string keyId) => KeyPrefix + keyId;
    public static string ForTenant(Guid tenantId) => TenantPrefix + tenantId;

    public static bool TryParseKey(string payload, out string keyId)
    {
        keyId = payload.StartsWith(KeyPrefix, StringComparison.Ordinal) ? payload[KeyPrefix.Length..] : "";
        return keyId.Length > 0;
    }

    public static bool IsTenant(string payload) => payload.StartsWith(TenantPrefix, StringComparison.Ordinal);
}
