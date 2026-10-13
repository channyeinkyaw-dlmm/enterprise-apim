using EnterpriseApim.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EnterpriseApim.Gateway.Usage;

public sealed class KeyUsageFlushService(
    KeyUsageTracker tracker,
    IServiceScopeFactory scopes,
    IOptions<GatewayOptions> options,
    ILogger<KeyUsageFlushService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, options.Value.UsageFlushSeconds));
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await FlushAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await FlushAsync(cancellationToken); // don't lose the last interval on shutdown
    }

    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        var pending = tracker.Drain();
        if (pending.Count == 0) return;

        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApimDbContext>();
            foreach (var (apiKeyId, usedAt) in pending)
            {
                DateTimeOffset? lastUsed = usedAt.ToUniversalTime();
                await db.ApiKeys
                    .Where(k => k.Id == apiKeyId && (k.LastUsedAt == null || k.LastUsedAt < lastUsed))
                    .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, lastUsed), cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to record key usage for {Count} keys", pending.Count);
        }
    }
}
