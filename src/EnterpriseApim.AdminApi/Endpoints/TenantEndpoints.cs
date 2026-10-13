using EnterpriseApim.Core;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseApim.AdminApi.Endpoints;

public static class TenantEndpoints
{
    public static RouteGroupBuilder MapTenantEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/tenants").WithTags("Clients");
        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPost("/", CreateAsync);
        group.MapPut("/{id:guid}", UpdateAsync);
        group.MapDelete("/{id:guid}", DeleteAsync);
        return api;
    }

    private static async Task<IResult> ListAsync(ApimDbContext db, TimeProvider time, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var tenants = await Project(db.Tenants.AsNoTracking().OrderBy(t => t.Name), now).ToListAsync(ct);
        return TypedResults.Ok(tenants);
    }

    private static async Task<IResult> GetAsync(Guid id, ApimDbContext db, TimeProvider time, CancellationToken ct)
    {
        var tenant = await Project(db.Tenants.AsNoTracking().Where(t => t.Id == id), time.GetUtcNow())
            .FirstOrDefaultAsync(ct);
        return tenant is null ? NotFound() : TypedResults.Ok(tenant);
    }

    private static async Task<IResult> CreateAsync(TenantRequest request, ApimDbContext db, TimeProvider time,
        CancellationToken ct)
    {
        var errors = Validate(request);
        if (!errors.IsValid) return errors.ToResult();

        var name = request.Name!.Trim();
        if (await db.Tenants.AnyAsync(t => t.Name == name, ct))
            return Conflict($"A client named \"{name}\" already exists.");

        var now = time.GetUtcNow();
        var tenant = new Tenant { Id = Guid.CreateVersion7(), CreatedAt = now };
        Apply(tenant, request, now);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/tenants/{tenant.Id}", ToResponse(tenant, activeKeyCount: 0));
    }

    private static async Task<IResult> UpdateAsync(Guid id, TenantRequest request, ApimDbContext db,
        ChangeNotifier notifier, TimeProvider time, CancellationToken ct)
    {
        var errors = Validate(request);
        if (!errors.IsValid) return errors.ToResult();

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null) return NotFound();

        var name = request.Name!.Trim();
        if (await db.Tenants.AnyAsync(t => t.Name == name && t.Id != id, ct))
            return Conflict($"A client named \"{name}\" already exists.");

        var now = time.GetUtcNow();
        Apply(tenant, request, now);
        await db.SaveChangesAsync(ct);
        await notifier.TenantChangedAsync(id, ct);

        return await GetAsync(id, db, time, ct);
    }

    private static async Task<IResult> DeleteAsync(Guid id, ApimDbContext db, ChangeNotifier notifier,
        CancellationToken ct)
    {
        // API keys are removed by the cascading foreign key.
        var deleted = await db.Tenants.Where(t => t.Id == id).ExecuteDeleteAsync(ct);
        if (deleted == 0) return NotFound();

        await notifier.TenantChangedAsync(id, ct);
        return TypedResults.NoContent();
    }

    private static ValidationErrors Validate(TenantRequest request)
    {
        var errors = new ValidationErrors();

        if (string.IsNullOrWhiteSpace(request.Name))
            errors.Add("name", "Enter a client name.");
        else if (request.Name.Trim().Length > 200)
            errors.Add("name", "Use 200 characters or fewer.");

        if (request.Description?.Length > 1000)
            errors.Add("description", "Use 1000 characters or fewer.");

        if (request.RateLimitPerMinute is <= 0)
            errors.Add("rateLimitPerMinute", "Enter a positive number, or leave empty for no limit.");

        foreach (var range in Rules.NormalizeList(request.AllowedIpRanges))
        {
            if (!IpRanges.TryParse(range, out _))
                errors.Add("allowedIpRanges", $"\"{range}\" is not an IP address or CIDR range like 203.0.113.0/24.");
        }

        return errors;
    }

    private static void Apply(Tenant tenant, TenantRequest request, DateTimeOffset now)
    {
        tenant.Name = request.Name!.Trim();
        tenant.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        tenant.IsActive = request.IsActive;
        tenant.RateLimitPerMinute = request.RateLimitPerMinute;
        tenant.AllowedIpRanges = Rules.NormalizeList(request.AllowedIpRanges);
        tenant.UpdatedAt = now;
    }

    private static IQueryable<TenantResponse> Project(IQueryable<Tenant> query, DateTimeOffset now) =>
        query.Select(t => new TenantResponse(
            t.Id, t.Name, t.Description, t.IsActive, t.RateLimitPerMinute, t.AllowedIpRanges,
            t.ApiKeys.Count(k => k.RevokedAt == null && (k.ExpiresAt == null || k.ExpiresAt > now)),
            t.CreatedAt, t.UpdatedAt));

    private static TenantResponse ToResponse(Tenant t, int activeKeyCount) => new(
        t.Id, t.Name, t.Description, t.IsActive, t.RateLimitPerMinute, t.AllowedIpRanges,
        activeKeyCount, t.CreatedAt, t.UpdatedAt);

    private static IResult NotFound() =>
        TypedResults.Problem(title: "Client not found", statusCode: StatusCodes.Status404NotFound);

    private static IResult Conflict(string detail) =>
        TypedResults.Problem(title: "Conflict", detail: detail, statusCode: StatusCodes.Status409Conflict);
}
