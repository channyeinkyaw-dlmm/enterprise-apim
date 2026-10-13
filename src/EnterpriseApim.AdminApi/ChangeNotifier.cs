using EnterpriseApim.Core;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseApim.AdminApi;

/// <summary>Tells every gateway instance about a change via PostgreSQL NOTIFY.</summary>
public sealed class ChangeNotifier(ApimDbContext db, ILogger<ChangeNotifier> logger)
{
    public Task ConfigChangedAsync(CancellationToken ct) => NotifyAsync(ChangeNotifications.Config, ct);

    public Task KeyChangedAsync(string keyId, CancellationToken ct) => NotifyAsync(ChangeNotifications.ForKey(keyId), ct);

    public Task TenantChangedAsync(Guid tenantId, CancellationToken ct) =>
        NotifyAsync(ChangeNotifications.ForTenant(tenantId), ct);

    public Task ResyncAsync(CancellationToken ct) => NotifyAsync(ChangeNotifications.Resync, ct);

    private async Task NotifyAsync(string payload, CancellationToken ct)
    {
        try
        {
            await db.Database.ExecuteSqlAsync($"SELECT pg_notify({ChangeNotifications.Channel}, {payload})", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The change is saved; gateways will still pick it up on their periodic refresh.
            logger.LogWarning(ex, "Could not notify gateways of change {Payload}", payload);
        }
    }
}
