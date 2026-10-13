using EnterpriseApim.AdminApi.Auth;
using EnterpriseApim.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseApim.AdminApi.Endpoints;

/// <summary>
/// Admin console accounts. Every admin can manage every other admin, but not disable or delete themselves,
/// so at least one active admin always remains.
/// </summary>
public static class AdminUserEndpoints
{
    public static RouteGroupBuilder MapAdminUserEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/users").WithTags("Admin users");
        group.MapGet("/", ListAsync);
        group.MapPost("/", CreateAsync);
        group.MapPost("/{id:guid}/enable", (Guid id, HttpContext http, ApimDbContext db, TimeProvider time,
            ILogger<AdminUser> logger, CancellationToken ct) => SetActiveAsync(id, true, http, db, time, logger, ct));
        group.MapPost("/{id:guid}/disable", (Guid id, HttpContext http, ApimDbContext db, TimeProvider time,
            ILogger<AdminUser> logger, CancellationToken ct) => SetActiveAsync(id, false, http, db, time, logger, ct));
        group.MapPost("/{id:guid}/reset-password", ResetPasswordAsync);
        group.MapDelete("/{id:guid}", DeleteAsync);
        return api;
    }

    private static async Task<IResult> ListAsync(ApimDbContext db, TimeProvider time, CancellationToken ct)
    {
        var users = await db.AdminUsers.AsNoTracking().OrderBy(u => u.Username).ToListAsync(ct);
        var now = time.GetUtcNow();
        return TypedResults.Ok(users.Select(u => ToResponse(u, now)));
    }

    private static async Task<IResult> CreateAsync(CreateAdminUserRequest request, HttpContext http, ApimDbContext db,
        IPasswordHasher<AdminUser> hasher, TimeProvider time, ILogger<AdminUser> logger, CancellationToken ct)
    {
        var username = AdminAuth.NormalizeUsername(request.Username);
        var errors = new ValidationErrors();
        AdminAuth.ValidateUsername(username, errors);
        AdminAuth.ValidateNewPassword(request.Password, username, errors);
        if (!errors.IsValid) return errors.ToResult();

        if (await db.AdminUsers.AnyAsync(u => u.Username == username, ct))
            return Conflict($"An admin named \"{username}\" already exists.");

        var now = time.GetUtcNow();
        var user = new AdminUser
        {
            Id = Guid.CreateVersion7(),
            Username = username,
            SecurityStamp = AdminAuth.NewSecurityStamp(),
            MustChangePassword = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        user.PasswordHash = hasher.HashPassword(user, request.Password!);
        db.AdminUsers.Add(user);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("{Actor} added admin user {Username}", http.User.Identity?.Name, username);
        return TypedResults.Created($"/api/users/{user.Id}", ToResponse(user, now));
    }

    private static async Task<IResult> SetActiveAsync(Guid id, bool active, HttpContext http, ApimDbContext db,
        TimeProvider time, ILogger<AdminUser> logger, CancellationToken ct)
    {
        if (!active && id == http.User.UserId())
            return Conflict("You can't disable your own account.");

        var user = await db.AdminUsers.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();

        var now = time.GetUtcNow();
        user.IsActive = active;
        user.FailedSignInCount = 0;
        user.LockedUntil = null;
        user.UpdatedAt = now;
        if (!active) user.SecurityStamp = AdminAuth.NewSecurityStamp(); // ends their sessions
        await db.SaveChangesAsync(ct);

        logger.LogInformation("{Actor} {Action} admin user {Username}",
            http.User.Identity?.Name, active ? "enabled" : "disabled", user.Username);
        return TypedResults.Ok(ToResponse(user, now));
    }

    /// <summary>Sets a temporary password. The user must replace it at next sign-in; their sessions end.</summary>
    private static async Task<IResult> ResetPasswordAsync(Guid id, ResetPasswordRequest request, HttpContext http,
        ApimDbContext db, IPasswordHasher<AdminUser> hasher, TimeProvider time, ILogger<AdminUser> logger,
        CancellationToken ct)
    {
        if (id == http.User.UserId())
            return Conflict("Use Change password to change your own password.");

        var user = await db.AdminUsers.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();

        var errors = new ValidationErrors();
        AdminAuth.ValidateNewPassword(request.Password, user.Username, errors);
        if (!errors.IsValid) return errors.ToResult();

        var now = time.GetUtcNow();
        user.PasswordHash = hasher.HashPassword(user, request.Password!);
        user.SecurityStamp = AdminAuth.NewSecurityStamp();
        user.MustChangePassword = true;
        user.FailedSignInCount = 0;
        user.LockedUntil = null;
        user.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("{Actor} reset the password of admin user {Username}", http.User.Identity?.Name, user.Username);
        return TypedResults.Ok(ToResponse(user, now));
    }

    private static async Task<IResult> DeleteAsync(Guid id, HttpContext http, ApimDbContext db,
        ILogger<AdminUser> logger, CancellationToken ct)
    {
        if (id == http.User.UserId())
            return Conflict("You can't delete your own account.");

        var deleted = await db.AdminUsers.Where(u => u.Id == id).ExecuteDeleteAsync(ct);
        if (deleted == 0) return NotFound();

        logger.LogInformation("{Actor} deleted admin user {Id}", http.User.Identity?.Name, id);
        return TypedResults.NoContent();
    }

    private static AdminUserResponse ToResponse(AdminUser u, DateTimeOffset now) => new(
        u.Id, u.Username, u.IsActive, u.MustChangePassword, u.LockedUntil > now, u.LastSignInAt, u.CreatedAt);

    private static IResult NotFound() =>
        TypedResults.Problem(title: "Admin user not found", statusCode: StatusCodes.Status404NotFound);

    private static IResult Conflict(string detail) =>
        TypedResults.Problem(title: "Conflict", detail: detail, statusCode: StatusCodes.Status409Conflict);
}
