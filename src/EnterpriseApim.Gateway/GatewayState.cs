namespace EnterpriseApim.Gateway;

/// <summary>Tracks whether routes have been loaded, for the readiness probe.</summary>
public sealed class GatewayState
{
    private volatile Snapshot? _snapshot;

    public bool IsReady => _snapshot is not null;
    public Snapshot? Current => _snapshot;

    public void MarkLoaded(int routes, int clusters, int skipped) =>
        _snapshot = new Snapshot(DateTimeOffset.UtcNow, routes, clusters, skipped);

    public sealed record Snapshot(DateTimeOffset LoadedAt, int Routes, int Clusters, int Skipped);
}
