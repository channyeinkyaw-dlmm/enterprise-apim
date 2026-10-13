using System.Text.Json;
using EnterpriseApim.Core;
using EnterpriseApim.Gateway.Auth;
using Microsoft.EntityFrameworkCore;
using Yarp.ReverseProxy.Configuration;

namespace EnterpriseApim.Gateway.Configuration;

/// <summary>
/// Reads routes and clusters from PostgreSQL and hands them to YARP. Each item is validated
/// individually, so one bad route is skipped (and logged) instead of blocking every update.
/// </summary>
public sealed class DbProxyConfigLoader(
    IServiceScopeFactory scopes,
    InMemoryConfigProvider provider,
    IConfigValidator validator,
    GatewayState state,
    ILogger<DbProxyConfigLoader> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task ReloadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApimDbContext>();

            var clusterRows = await db.Clusters.AsNoTracking()
                .Include(c => c.Destinations)
                .ToListAsync(cancellationToken);
            var routeRows = await db.Routes.AsNoTracking()
                .Where(r => r.IsEnabled)
                .ToListAsync(cancellationToken);

            var skipped = 0;
            var clusters = new List<ClusterConfig>();
            foreach (var row in clusterRows)
            {
                var cluster = ToClusterConfig(row);
                var errors = await validator.ValidateClusterAsync(cluster);
                if (errors.Count > 0)
                {
                    LogInvalid("cluster", row.Id, errors);
                    skipped++;
                    continue;
                }
                clusters.Add(cluster);
            }

            var clusterIds = clusters.Select(c => c.ClusterId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var routes = new List<RouteConfig>();
            foreach (var row in routeRows)
            {
                if (!clusterIds.Contains(row.ClusterId))
                {
                    logger.LogWarning("Skipping route {RouteId}: cluster {ClusterId} is missing or invalid",
                        row.Id, row.ClusterId);
                    skipped++;
                    continue;
                }

                RouteConfig route;
                try
                {
                    route = ToRouteConfig(row);
                }
                catch (JsonException ex)
                {
                    logger.LogWarning(ex, "Skipping route {RouteId}: transforms are not valid JSON", row.Id);
                    skipped++;
                    continue;
                }

                var errors = await validator.ValidateRouteAsync(route);
                if (errors.Count > 0)
                {
                    LogInvalid("route", row.Id, errors);
                    skipped++;
                    continue;
                }
                routes.Add(route);
            }

            provider.Update(routes, clusters);
            state.MarkLoaded(routes.Count, clusters.Count, skipped);
            logger.LogInformation("Loaded {Routes} routes and {Clusters} clusters ({Skipped} skipped)",
                routes.Count, clusters.Count, skipped);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void LogInvalid(string kind, string id, IList<Exception> errors) =>
        logger.LogWarning("Skipping {Kind} {Id}: {Errors}", kind, id, string.Join("; ", errors.Select(e => e.Message)));

    internal static RouteConfig ToRouteConfig(GatewayRoute row) => new()
    {
        RouteId = row.Id,
        ClusterId = row.ClusterId,
        Order = row.Order,
        Match = new RouteMatch
        {
            Path = row.Path,
            Methods = row.Methods.Length > 0 ? row.Methods : null
        },
        // null => the fallback policy (a valid client key) applies.
        AuthorizationPolicy = row.AllowAnonymous
            ? "Anonymous"
            : string.IsNullOrWhiteSpace(row.RequiredScope)
                ? null
                : ScopePolicyProvider.PolicyNameFor(row.RequiredScope.Trim()),
        Transforms = string.IsNullOrWhiteSpace(row.TransformsJson)
            ? null
            : JsonSerializer.Deserialize<List<Dictionary<string, string>>>(row.TransformsJson)
    };

    internal static ClusterConfig ToClusterConfig(GatewayCluster row) => new()
    {
        ClusterId = row.Id,
        LoadBalancingPolicy = string.IsNullOrWhiteSpace(row.LoadBalancingPolicy) ? null : row.LoadBalancingPolicy,
        HealthCheck = row.HealthCheckEnabled
            ? new HealthCheckConfig
            {
                Active = new ActiveHealthCheckConfig
                {
                    Enabled = true,
                    Interval = TimeSpan.FromSeconds(Math.Max(1, row.HealthCheckIntervalSeconds ?? 15)),
                    Timeout = TimeSpan.FromSeconds(5),
                    Policy = "ConsecutiveFailures",
                    Path = string.IsNullOrWhiteSpace(row.HealthCheckPath) ? "/health" : row.HealthCheckPath
                }
            }
            : null,
        Destinations = row.Destinations.ToDictionary(
            d => d.Name,
            d => new DestinationConfig { Address = d.Address },
            StringComparer.OrdinalIgnoreCase)
    };
}
