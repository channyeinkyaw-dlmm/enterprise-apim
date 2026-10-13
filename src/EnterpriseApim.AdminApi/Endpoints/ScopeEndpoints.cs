using EnterpriseApim.Core;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseApim.AdminApi.Endpoints;

/// <summary>
/// The catalog of scopes that routes can require and keys can carry. A scope's name can't change, because
/// partners' keys carry it; a scope can only be deleted when no route and no active key uses it.
/// </summary>
public static class ScopeEndpoints
{
    public static RouteGroupBuilder MapScopeEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/scopes").WithTags("Scopes");
        group.MapGet("/", ListAsync);
        group.MapPost("/", CreateAsync);
        group.MapPut("/{name}", UpdateAsync);
        group.MapDelete("/{name}", DeleteAsync);
        return api;
    }

    private static async Task<IResult> ListAsync(ApimDbContext db, TimeProvider time, CancellationToken ct)
    {
        var scopes = await Project(db, db.Scopes.AsNoTracking().OrderBy(s => s.Name), time.GetUtcNow()).ToListAsync(ct);
        return TypedResults.Ok(scopes);
    }

    private static async Task<IResult> CreateAsync(ScopeRequest request, ApimDbContext db, TimeProvider time,
        CancellationToken ct)
    {
        var name = request.Name?.Trim() ?? "";
        var errors = new ValidationErrors();
        if (name == ClientClaims.AllScopes || !Rules.IsValidScope(name))
            errors.Add("name", "Use up to 100 letters, digits, '.', '-', ':' or '_', for example orders.read.");
        ValidateDescription(request.Description, errors);
        if (!errors.IsValid) return errors.ToResult();

        if (await db.Scopes.AnyAsync(s => s.Name == name, ct))
            return Conflict($"The scope \"{name}\" already exists.");

        var now = time.GetUtcNow();
        var scope = new Scope { Name = name, Description = Clean(request.Description), CreatedAt = now, UpdatedAt = now };
        db.Scopes.Add(scope);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/scopes/{Uri.EscapeDataString(name)}",
            new ScopeResponse(scope.Name, scope.Description, 0, 0, scope.CreatedAt, scope.UpdatedAt));
    }

    /// <summary>Changes the description. The name is fixed.</summary>
    private static async Task<IResult> UpdateAsync(string name, ScopeRequest request, ApimDbContext db,
        TimeProvider time, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        ValidateDescription(request.Description, errors);
        if (!errors.IsValid) return errors.ToResult();

        var scope = await db.Scopes.FirstOrDefaultAsync(s => s.Name == name, ct);
        if (scope is null) return NotFound();

        var now = time.GetUtcNow();
        scope.Description = Clean(request.Description);
        scope.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        var response = await Project(db, db.Scopes.AsNoTracking().Where(s => s.Name == name), now).SingleAsync(ct);
        return TypedResults.Ok(response);
    }

    private static async Task<IResult> DeleteAsync(string name, ApimDbContext db, TimeProvider time,
        CancellationToken ct)
    {
        var usage = await Project(db, db.Scopes.AsNoTracking().Where(s => s.Name == name), time.GetUtcNow())
            .FirstOrDefaultAsync(ct);
        if (usage is null) return NotFound();

        if (usage.RouteCount > 0 || usage.ActiveKeyCount > 0)
        {
            return Conflict(
                $"\"{name}\" is required by {Plural(usage.RouteCount, "route")} and carried by " +
                $"{Plural(usage.ActiveKeyCount, "active key")}. Change the routes and revoke or replace the keys first.");
        }

        await db.Scopes.Where(s => s.Name == name).ExecuteDeleteAsync(ct);
        return TypedResults.NoContent();
    }

    private static IQueryable<ScopeResponse> Project(ApimDbContext db, IQueryable<Scope> query, DateTimeOffset now) =>
        query.Select(s => new ScopeResponse(
            s.Name,
            s.Description,
            db.Routes.Count(r => r.RequiredScope == s.Name),
            db.ApiKeys.Count(k => k.Scopes.Contains(s.Name) && k.RevokedAt == null &&
                                  (k.ExpiresAt == null || k.ExpiresAt > now)),
            s.CreatedAt,
            s.UpdatedAt));

    private static void ValidateDescription(string? description, ValidationErrors errors)
    {
        if (description?.Trim().Length > 500)
            errors.Add("description", "Use 500 characters or fewer.");
    }

    private static string? Clean(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    private static IResult NotFound() =>
        TypedResults.Problem(title: "Scope not found", statusCode: StatusCodes.Status404NotFound);

    private static IResult Conflict(string detail) =>
        TypedResults.Problem(title: "Conflict", detail: detail, statusCode: StatusCodes.Status409Conflict);
}
