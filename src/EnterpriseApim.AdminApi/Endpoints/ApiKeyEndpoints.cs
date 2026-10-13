using EnterpriseApim.Core;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseApim.AdminApi.Endpoints;

public static class ApiKeyEndpoints
{
    public static RouteGroupBuilder MapApiKeyEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/tenants/{tenantId:guid}/keys").WithTags("Access keys");
        group.MapGet("/", ListAsync);
        group.MapPost("/", CreateAsync);
        group.MapPut("/{keyId}", UpdateAsync);
        group.MapPost("/{keyId}/revoke", RevokeAsync);
        return api;
    }

    private static async Task<IResult> ListAsync(Guid tenantId, ApimDbContext db, TimeProvider time,
        CancellationToken ct)
    {
        if (!await db.Tenants.AnyAsync(t => t.Id == tenantId, ct)) return TenantNotFound();

        var keys = await db.ApiKeys.AsNoTracking()
            .Where(k => k.TenantId == tenantId)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(ct);

        var now = time.GetUtcNow();
        return TypedResults.Ok(keys.Select(k => ToResponse(k, now)));
    }

    private static async Task<IResult> CreateAsync(Guid tenantId, CreateApiKeyRequest request, ApimDbContext db,
        TimeProvider time, CancellationToken ct)
    {
        if (!await db.Tenants.AnyAsync(t => t.Id == tenantId, ct)) return TenantNotFound();

        var now = time.GetUtcNow();
        var scopes = Rules.NormalizeList(request.Scopes);
        var errors = await ValidateAsync(request.Name, scopes, db, ct);
        if (request.ExpiresAt is { } expiresAt && expiresAt <= now)
            errors.Add("expiresAt", "Choose an expiry date in the future.");
        if (!errors.IsValid) return errors.ToResult();

        var (plainKey, keyId, secretHash) = ApiKeyFormat.Generate();
        var key = new ApiKey
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            KeyId = keyId,
            SecretHash = secretHash,
            Name = request.Name!.Trim(),
            Scopes = scopes,
            CreatedAt = now,
            // PostgreSQL timestamptz requires UTC offsets.
            ExpiresAt = request.ExpiresAt?.ToUniversalTime()
        };

        db.ApiKeys.Add(key);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/tenants/{tenantId}/keys/{keyId}",
            new CreatedApiKeyResponse(ToResponse(key, now), plainKey));
    }

    /// <summary>
    /// Changes a key's name, scopes or expiry. The secret stays the same, so the client keeps using its key.
    /// Revoked keys can't be edited. Setting a future expiry on an expired key makes it work again.
    /// </summary>
    private static async Task<IResult> UpdateAsync(Guid tenantId, string keyId, UpdateApiKeyRequest request,
        ApimDbContext db, ChangeNotifier notifier, TimeProvider time, CancellationToken ct)
    {
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.TenantId == tenantId && k.KeyId == keyId, ct);
        if (key is null)
            return TypedResults.Problem(title: "Key not found", statusCode: StatusCodes.Status404NotFound);
        if (key.RevokedAt is not null)
        {
            return TypedResults.Problem(title: "Conflict", detail: "A revoked key can't be edited. Issue a new key instead.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var now = time.GetUtcNow();
        var scopes = Rules.NormalizeList(request.Scopes);
        var expiresAt = request.ExpiresAt?.ToUniversalTime();
        var errors = await ValidateAsync(request.Name, scopes, db, ct);
        // An unchanged past expiry is fine (editing an expired key's name); a new one must be in the future.
        if (expiresAt is { } newExpiry && newExpiry <= now && newExpiry != key.ExpiresAt)
            errors.Add("expiresAt", "Choose an expiry date in the future.");
        if (!errors.IsValid) return errors.ToResult();

        key.Name = request.Name!.Trim();
        key.Scopes = scopes;
        key.ExpiresAt = expiresAt;
        await db.SaveChangesAsync(ct);

        // Gateways cache keys; evict it everywhere so the new scopes and expiry apply right away.
        await notifier.KeyChangedAsync(keyId, ct);
        return TypedResults.Ok(ToResponse(key, now));
    }

    private static async Task<ValidationErrors> ValidateAsync(string? name, string[] scopes, ApimDbContext db,
        CancellationToken ct)
    {
        var errors = new ValidationErrors();

        if (string.IsNullOrWhiteSpace(name))
            errors.Add("name", "Name the key after where it is used, for example \"Production ERP\".");
        else if (name.Trim().Length > 200)
            errors.Add("name", "Use 200 characters or fewer.");

        var known = await db.Scopes.Where(s => scopes.Contains(s.Name)).Select(s => s.Name).ToListAsync(ct);
        foreach (var scope in scopes.Where(s => s != ClientClaims.AllScopes && !known.Contains(s)))
            errors.Add("scopes", $"There is no scope named \"{scope}\". Add it on the Scopes page first.");

        return errors;
    }

    private static async Task<IResult> RevokeAsync(Guid tenantId, string keyId, ApimDbContext db,
        ChangeNotifier notifier, TimeProvider time, CancellationToken ct)
    {
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.TenantId == tenantId && k.KeyId == keyId, ct);
        if (key is null)
            return TypedResults.Problem(title: "Key not found", statusCode: StatusCodes.Status404NotFound);

        var now = time.GetUtcNow();
        if (key.RevokedAt is null)
        {
            key.RevokedAt = now;
            await db.SaveChangesAsync(ct);
        }

        // Evict it from every gateway's cache right away.
        await notifier.KeyChangedAsync(keyId, ct);
        return TypedResults.Ok(ToResponse(key, now));
    }

    private static ApiKeyResponse ToResponse(ApiKey k, DateTimeOffset now) => new(
        k.Id, k.KeyId, k.Name, k.Scopes,
        k.RevokedAt is not null ? "revoked" : k.ExpiresAt <= now ? "expired" : "active",
        k.CreatedAt, k.ExpiresAt, k.RevokedAt, k.LastUsedAt);

    private static IResult TenantNotFound() =>
        TypedResults.Problem(title: "Client not found", statusCode: StatusCodes.Status404NotFound);
}
