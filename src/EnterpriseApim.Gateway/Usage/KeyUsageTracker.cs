using System.Collections.Concurrent;

namespace EnterpriseApim.Gateway.Usage;

/// <summary>Collects "last used" timestamps in memory so they can be written in batches.</summary>
public sealed class KeyUsageTracker
{
    private ConcurrentDictionary<Guid, DateTimeOffset> _pending = new();

    public void Record(Guid apiKeyId, DateTimeOffset usedAt) => _pending[apiKeyId] = usedAt;

    public IReadOnlyDictionary<Guid, DateTimeOffset> Drain() =>
        Interlocked.Exchange(ref _pending, new ConcurrentDictionary<Guid, DateTimeOffset>());
}
