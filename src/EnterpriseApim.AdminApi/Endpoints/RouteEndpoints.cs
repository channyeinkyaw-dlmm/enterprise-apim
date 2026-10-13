using EnterpriseApim.Core;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseApim.AdminApi.Endpoints;

public static class RouteEndpoints
{
    public static RouteGroupBuilder MapRouteEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/routes").WithTags("Routes");
        group.MapGet("/", ListAsync);
        group.MapGet("/{id}", GetAsync);
        group.MapPost("/", CreateAsync);
        group.MapPut("/{id}", UpdateAsync);
        group.MapDelete("/{id}", DeleteAsync);
        return api;
    }

    private static async Task<IResult> ListAsync(ApimDbContext db, CancellationToken ct)
    {
        var routes = await db.Routes.AsNoTracking()
            .OrderBy(r => r.Order ?? 0).ThenBy(r => r.Path)
            .ToListAsync(ct);
        return TypedResults.Ok(routes.Select(ToResponse));
    }

    private static async Task<IResult> GetAsync(string id, ApimDbContext db, CancellationToken ct)
    {
        var route = await db.Routes.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        return route is null ? NotFound() : TypedResults.Ok(ToResponse(route));
    }

    private static async Task<IResult> CreateAsync(RouteRequest request, ApimDbContext db, ChangeNotifier notifier,
        TimeProvider time, CancellationToken ct)
    {
        var errors = await ValidateAsync(request, db, validateId: true, ct);
        if (!errors.IsValid) return errors.ToResult();

        var id = request.Id!.Trim();
        if (await db.Routes.AnyAsync(r => r.Id == id, ct))
            return Conflict($"A route with the ID \"{id}\" already exists.");

        var route = new GatewayRoute { Id = id };
        Apply(route, request, time.GetUtcNow());
        db.Routes.Add(route);
        await db.SaveChangesAsync(ct);
        await notifier.ConfigChangedAsync(ct);

        return TypedResults.Created($"/api/routes/{id}", ToResponse(route));
    }

    private static async Task<IResult> UpdateAsync(string id, RouteRequest request, ApimDbContext db,
        ChangeNotifier notifier, TimeProvider time, CancellationToken ct)
    {
        var errors = await ValidateAsync(request, db, validateId: false, ct);
        if (!errors.IsValid) return errors.ToResult();

        var route = await db.Routes.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (route is null) return NotFound();

        Apply(route, request, time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        await notifier.ConfigChangedAsync(ct);

        return TypedResults.Ok(ToResponse(route));
    }

    private static async Task<IResult> DeleteAsync(string id, ApimDbContext db, ChangeNotifier notifier,
        CancellationToken ct)
    {
        var deleted = await db.Routes.Where(r => r.Id == id).ExecuteDeleteAsync(ct);
        if (deleted == 0) return NotFound();

        await notifier.ConfigChangedAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<ValidationErrors> ValidateAsync(RouteRequest request, ApimDbContext db,
        bool validateId, CancellationToken ct)
    {
        var errors = new ValidationErrors();

        if (validateId && (request.Id is null || !Rules.Identifier().IsMatch(request.Id.Trim())))
            errors.Add("id", "Use lowercase letters, digits and dashes, for example orders-read.");

        var clusterId = request.ClusterId?.Trim();
        if (string.IsNullOrEmpty(clusterId))
            errors.Add("clusterId", "Choose the backend this route sends traffic to.");
        else if (!await db.Clusters.AnyAsync(c => c.Id == clusterId, ct))
            errors.Add("clusterId", $"There is no backend with the ID \"{clusterId}\".");

        Rules.ValidateRoutePath(request.Path?.Trim(), errors);

        foreach (var method in Rules.NormalizeList(request.Methods))
        {
            if (!Rules.HttpMethods.Contains(method.ToUpperInvariant()))
                errors.Add("methods", $"\"{method}\" is not a supported HTTP method.");
        }

        var scope = request.RequiredScope?.Trim();
        if (!string.IsNullOrEmpty(scope))
        {
            if (request.AllowAnonymous)
                errors.Add("requiredScope", "A public route can't require a scope. Choose one or the other.");
            else if (!await db.Scopes.AnyAsync(s => s.Name == scope, ct))
                errors.Add("requiredScope", $"There is no scope named \"{scope}\". Add it on the Scopes page first.");
        }

        Rules.ValidateTransforms(request.TransformsJson, errors);

        if (request.Description?.Length > 1000)
            errors.Add("description", "Use 1000 characters or fewer.");

        return errors;
    }

    private static void Apply(GatewayRoute route, RouteRequest request, DateTimeOffset now)
    {
        route.ClusterId = request.ClusterId!.Trim();
        route.Path = request.Path!.Trim();
        route.Methods = Rules.NormalizeList(request.Methods).Select(m => m.ToUpperInvariant()).Distinct().ToArray();
        route.Order = request.Order;
        route.AllowAnonymous = request.AllowAnonymous;
        route.RequiredScope = request.AllowAnonymous || string.IsNullOrWhiteSpace(request.RequiredScope)
            ? null
            : request.RequiredScope.Trim();
        route.TransformsJson = string.IsNullOrWhiteSpace(request.TransformsJson) ? null : request.TransformsJson.Trim();
        route.IsEnabled = request.IsEnabled;
        route.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        route.UpdatedAt = now;
    }

    private static RouteResponse ToResponse(GatewayRoute r) => new(
        r.Id, r.ClusterId, r.Path, r.Methods, r.Order, r.RequiredScope, r.AllowAnonymous,
        r.TransformsJson, r.IsEnabled, r.Description, r.UpdatedAt);

    private static IResult NotFound() =>
        TypedResults.Problem(title: "Route not found", statusCode: StatusCodes.Status404NotFound);

    private static IResult Conflict(string detail) =>
        TypedResults.Problem(title: "Conflict", detail: detail, statusCode: StatusCodes.Status409Conflict);
}
