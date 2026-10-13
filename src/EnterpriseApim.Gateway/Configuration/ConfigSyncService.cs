using System.Threading.Channels;
using EnterpriseApim.Core;
using EnterpriseApim.Gateway.Auth;
using Microsoft.Extensions.Options;
using Npgsql;

namespace EnterpriseApim.Gateway.Configuration;

/// <summary>
/// Keeps this gateway instance in sync with the database:
/// LISTENs on a PostgreSQL channel for instant updates from the admin API, and reloads
/// periodically as a safety net in case a notification was missed.
/// </summary>
public sealed class ConfigSyncService(
    DbProxyConfigLoader loader,
    ApiKeyCache keyCache,
    IConfiguration configuration,
    IOptions<GatewayOptions> options,
    ILogger<ConfigSyncService> logger) : BackgroundService
{
    private readonly Channel<string> _events = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true });

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await InitialLoadAsync(stoppingToken);

        await Task.WhenAll(
            ListenAsync(stoppingToken),
            PeriodicRefreshAsync(stoppingToken),
            ProcessEventsAsync(stoppingToken));
    }

    private async Task InitialLoadAsync(CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await loader.ReloadAsync(ct);
                return;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Could not load gateway configuration yet; retrying in {Delay}", delay);
                await DelayAsync(delay, ct);
                delay = Backoff(delay);
            }
        }
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        var builder = new NpgsqlConnectionStringBuilder(
            configuration.GetConnectionString(ServiceCollectionExtensions.ConnectionStringName))
        {
            // Detect dead connections; an idle LISTEN connection could otherwise hang silently.
            KeepAlive = 30
        };

        var delay = TimeSpan.FromSeconds(1);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var connection = new NpgsqlConnection(builder.ConnectionString);
                await connection.OpenAsync(ct);
                connection.Notification += (_, e) => _events.Writer.TryWrite(e.Payload);

                await using (var listen = new NpgsqlCommand($"LISTEN {ChangeNotifications.Channel}", connection))
                    await listen.ExecuteNonQueryAsync(ct);

                logger.LogInformation("Listening for configuration changes on channel {Channel}",
                    ChangeNotifications.Channel);

                // Anything that changed while we were disconnected was missed; resync.
                _events.Writer.TryWrite(ChangeNotifications.Resync);
                delay = TimeSpan.FromSeconds(1);

                while (!ct.IsCancellationRequested)
                    await connection.WaitAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Change listener disconnected; reconnecting in {Delay}", delay);
                await DelayAsync(delay, ct);
                delay = Backoff(delay);
            }
        }
    }

    private async Task PeriodicRefreshAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, options.Value.ConfigRefreshSeconds));
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                _events.Writer.TryWrite(ChangeNotifications.Config);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private async Task ProcessEventsAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var payload in _events.Reader.ReadAllAsync(ct))
            {
                try
                {
                    await HandleAsync(payload, ct);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    logger.LogError(ex, "Failed to apply change notification {Payload}", payload);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private async Task HandleAsync(string payload, CancellationToken ct)
    {
        if (payload == ChangeNotifications.Resync)
        {
            keyCache.Clear();
            await loader.ReloadAsync(ct);
        }
        else if (payload == ChangeNotifications.Config)
        {
            await loader.ReloadAsync(ct);
        }
        else if (ChangeNotifications.TryParseKey(payload, out var keyId))
        {
            keyCache.Remove(keyId);
            logger.LogInformation("Evicted cached key {KeyId}", keyId);
        }
        else if (ChangeNotifications.IsTenant(payload))
        {
            // Tenant settings (active, IP ranges, rate limit) are cached on every key.
            keyCache.Clear();
            logger.LogInformation("Cleared key cache after tenant change ({Payload})", payload);
        }
    }

    private static TimeSpan Backoff(TimeSpan delay) =>
        TimeSpan.FromSeconds(Math.Min(30, delay.TotalSeconds * 2));

    private static async Task DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
