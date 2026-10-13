using EnterpriseApim.Core;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseApim.AdminApi.Endpoints;

public static class ClusterEndpoints
{
    public static RouteGroupBuilder MapClusterEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/clusters").WithTags("Backends");
        group.MapGet("/", ListAsync);
        group.MapGet("/{id}", GetAsync);
        group.MapPost("/", CreateAsync);
        group.MapPut("/{id}", UpdateAsync);
        group.MapDelete("/{id}", DeleteAsync);
        return api;
    }

    private static async Task<IResult> ListAsync(ApimDbContext db, CancellationToken ct)
    {
        var clusters = await db.Clusters.AsNoTracking()
            .Include(c => c.Destinations)
            .OrderBy(c => c.Id)
            .ToListAsync(ct);
        var routeCounts = await RouteCountsAsync(db, ct);
        return TypedResults.Ok(clusters.Select(c => ToResponse(c, routeCounts.GetValueOrDefault(c.Id))));
    }

    private static async Task<IResult> GetAsync(string id, ApimDbContext db, CancellationToken ct)
    {
        var cluster = await db.Clusters.AsNoTracking()
            .Include(c => c.Destinations)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cluster is null) return NotFound();

        var routeCount = await db.Routes.CountAsync(r => r.ClusterId == id, ct);
        return TypedResults.Ok(ToResponse(cluster, routeCount));
    }

    private static async Task<IResult> CreateAsync(ClusterRequest request, ApimDbContext db, ChangeNotifier notifier,
        TimeProvider time, CancellationToken ct)
    {
        var errors = Validate(request, validateId: true);
        if (!errors.IsValid) return errors.ToResult();

        var id = request.Id!.Trim();
        if (await db.Clusters.AnyAsync(c => c.Id == id, ct))
            return Conflict($"A backend with the ID \"{id}\" already exists.");

        var cluster = new GatewayCluster { Id = id };
        Apply(cluster, request, time.GetUtcNow());
        cluster.Destinations = ToDestinations(id, request);

        db.Clusters.Add(cluster);
        await db.SaveChangesAsync(ct);
        await notifier.ConfigChangedAsync(ct);

        return TypedResults.Created($"/api/clusters/{id}", ToResponse(cluster, 0));
    }

    private static async Task<IResult> UpdateAsync(string id, ClusterRequest request, ApimDbContext db,
        ChangeNotifier notifier, TimeProvider time, CancellationToken ct)
    {
        var errors = Validate(request, validateId: false);
        if (!errors.IsValid) return errors.ToResult();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var cluster = await db.Clusters.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cluster is null) return NotFound();

        // Replace destinations wholesale; simpler than diffing and keeps names unique.
        await db.Destinations.Where(d => d.ClusterId == id).ExecuteDeleteAsync(ct);
        Apply(cluster, request, time.GetUtcNow());
        db.Destinations.AddRange(ToDestinations(id, request));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        await notifier.ConfigChangedAsync(ct);
        return await GetAsync(id, db, ct);
    }

    private static async Task<IResult> DeleteAsync(string id, ApimDbContext db, ChangeNotifier notifier,
        CancellationToken ct)
    {
        var routeCount = await db.Routes.CountAsync(r => r.ClusterId == id, ct);
        if (routeCount > 0)
            return Conflict($"{routeCount} route(s) still send traffic to this backend. Delete them or point them elsewhere first.");

        var deleted = await db.Clusters.Where(c => c.Id == id).ExecuteDeleteAsync(ct);
        if (deleted == 0) return NotFound();

        await notifier.ConfigChangedAsync(ct);
        return TypedResults.NoContent();
    }

    private static ValidationErrors Validate(ClusterRequest request, bool validateId)
    {
        var errors = new ValidationErrors();

        if (validateId && (request.Id is null || !Rules.Identifier().IsMatch(request.Id.Trim())))
            errors.Add("id", "Use lowercase letters, digits and dashes, for example orders-service.");

        if (!string.IsNullOrWhiteSpace(request.LoadBalancingPolicy) &&
            !Rules.LoadBalancingPolicies.Contains(request.LoadBalancingPolicy))
            errors.Add("loadBalancingPolicy", $"Choose one of: {string.Join(", ", Rules.LoadBalancingPolicies)}.");

        if (request.HealthCheckEnabled)
        {
            if (!string.IsNullOrWhiteSpace(request.HealthCheckPath) && !request.HealthCheckPath.StartsWith('/'))
                errors.Add("healthCheckPath", "The health check path must start with /.");
            if (request.HealthCheckIntervalSeconds is < 1 or > 3600)
                errors.Add("healthCheckIntervalSeconds", "Use an interval between 1 and 3600 seconds.");
        }

        var destinations = request.Destinations ?? [];
        if (destinations.Length == 0)
            errors.Add("destinations", "Add at least one server address.");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var destination in destinations)
        {
            var name = destination.Name?.Trim() ?? "";
            if (!Rules.Identifier().IsMatch(name))
                errors.Add("destinations", $"Server name \"{name}\" must use lowercase letters, digits and dashes.");
            else if (!names.Add(name))
                errors.Add("destinations", $"Server name \"{name}\" is used twice.");

            if (!Uri.TryCreate(destination.Address?.Trim(), UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                errors.Add("destinations", $"\"{destination.Address}\" is not an http:// or https:// address.");
        }

        return errors;
    }

    private static void Apply(GatewayCluster cluster, ClusterRequest request, DateTimeOffset now)
    {
        cluster.LoadBalancingPolicy = string.IsNullOrWhiteSpace(request.LoadBalancingPolicy) ? null : request.LoadBalancingPolicy;
        cluster.HealthCheckEnabled = request.HealthCheckEnabled;
        cluster.HealthCheckPath = string.IsNullOrWhiteSpace(request.HealthCheckPath) ? null : request.HealthCheckPath.Trim();
        cluster.HealthCheckIntervalSeconds = request.HealthCheckIntervalSeconds;
        cluster.UpdatedAt = now;
    }

    private static List<ClusterDestination> ToDestinations(string clusterId, ClusterRequest request) =>
        (request.Destinations ?? []).Select(d => new ClusterDestination
        {
            Id = Guid.CreateVersion7(),
            ClusterId = clusterId,
            Name = d.Name!.Trim(),
            Address = d.Address!.Trim()
        }).ToList();

    private static async Task<Dictionary<string, int>> RouteCountsAsync(ApimDbContext db, CancellationToken ct) =>
        await db.Routes.GroupBy(r => r.ClusterId)
            .Select(g => new { ClusterId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ClusterId, x => x.Count, ct);

    private static ClusterResponse ToResponse(GatewayCluster c, int routeCount) => new(
        c.Id, c.LoadBalancingPolicy, c.HealthCheckEnabled, c.HealthCheckPath, c.HealthCheckIntervalSeconds,
        c.Destinations.OrderBy(d => d.Name).Select(d => new DestinationDto(d.Name, d.Address)).ToArray(),
        routeCount, c.UpdatedAt);

    private static IResult NotFound() =>
        TypedResults.Problem(title: "Backend not found", statusCode: StatusCodes.Status404NotFound);

    private static IResult Conflict(string detail) =>
        TypedResults.Problem(title: "Conflict", detail: detail, statusCode: StatusCodes.Status409Conflict);
}
