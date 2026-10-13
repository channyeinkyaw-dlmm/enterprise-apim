namespace EnterpriseApim.Gateway;

public sealed class GatewayOptions
{
    public const string Section = "Gateway";

    /// <summary>Safety-net reload interval, in case a NOTIFY was missed.</summary>
    public int ConfigRefreshSeconds { get; set; } = 60;

    /// <summary>How long a key lookup is cached. Revocations also evict immediately via NOTIFY.</summary>
    public int KeyCacheSeconds { get; set; } = 120;

    /// <summary>Per-IP limit for requests without a valid key (public routes, failed attempts).</summary>
    public int AnonymousRateLimitPerMinute { get; set; } = 120;

    /// <summary>How often "last used" timestamps are written to the database.</summary>
    public int UsageFlushSeconds { get; set; } = 60;

    /// <summary>
    /// IP addresses of load balancers in front of the gateway. When set, X-Forwarded-For from
    /// these proxies is trusted, which IP allowlists and per-IP limits need to see the real client.
    /// </summary>
    public string[] KnownProxies { get; set; } = [];
}
