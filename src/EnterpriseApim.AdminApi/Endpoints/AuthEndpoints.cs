using EnterpriseApim.AdminApi.Auth;
using EnterpriseApim.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EnterpriseApim.AdminApi.Endpoints;

public static class AuthEndpoints
{
    // Unknown usernames are checked against this hash so they take as long as real ones,
    // which keeps response times from revealing which usernames exist.
    private static readonly string DummyHash =
        new PasswordHasher<AdminUser>().HashPassword(new AdminUser(), Guid.NewGuid().ToString());

    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/auth").WithTags("Sign-in");
        group.MapPost("/sign-in", SignInAsync).AllowAnonymous().RequireRateLimiting(AdminAuth.SignInRateLimitPolicy);
        group.MapPost("/sign-out", SignOutAsync).AllowAnonymous();
        group.MapGet("/me", MeAsync).RequireAuthorization();
        group.MapPost("/password", ChangePasswordAsync).RequireAuthorization()
            .RequireRateLimiting(AdminAuth.SignInRateLimitPolicy);
        return api;
    }

    private static async Task<IResult> SignInAsync(SignInRequest request, HttpContext http, ApimDbContext db,
        IPasswordHasher<AdminUser> hasher, IOptions<AdminOptions> options, TimeProvider time,
        ILogger<AdminUser> logger, CancellationToken ct)
    {
        var username = AdminAuth.NormalizeUsername(request.Username);
        var password = request.Password ?? "";
        var now = time.GetUtcNow();

        var user = username.Length == 0 ? null : await db.AdminUsers.FirstOrDefaultAsync(u => u.Username == username, ct);
        if (user is null)
        {
            hasher.VerifyHashedPassword(new AdminUser(), DummyHash, password);
            logger.LogWarning("Failed sign-in for unknown user {Username}", Truncate(username));
            return InvalidCredentials();
        }

        if (user.LockedUntil > now)
            return Locked(user.LockedUntil.Value - now);

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
        {
            user.FailedSignInCount++;
            if (user.FailedSignInCount >= options.Value.MaxFailedSignIns)
            {
                user.FailedSignInCount = 0;
                user.LockedUntil = now.AddMinutes(options.Value.LockoutMinutes);
                logger.LogWarning("Locked admin user {Username} after repeated failed sign-ins", user.Username);
            }
            await db.SaveChangesAsync(ct);
            logger.LogWarning("Failed sign-in for {Username}", user.Username);
            return user.LockedUntil > now ? Locked(user.LockedUntil.Value - now) : InvalidCredentials();
        }

        if (!user.IsActive)
        {
            return TypedResults.Problem(title: "Account disabled",
                detail: "This account is disabled. Ask another admin to enable it.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = hasher.HashPassword(user, password);
        user.FailedSignInCount = 0;
        user.LockedUntil = null;
        user.LastSignInAt = now;
        await db.SaveChangesAsync(ct);

        await http.SignInAsync(AdminAuth.Scheme, AdminAuth.CreatePrincipal(user, now));
        logger.LogInformation("Admin user {Username} signed in", user.Username);
        return TypedResults.Ok(ToCurrentUser(user));
    }

    private static async Task<IResult> SignOutAsync(HttpContext http, ILogger<AdminUser> logger)
    {
        if (http.User.Identity?.IsAuthenticated == true)
            logger.LogInformation("Admin user {Username} signed out", http.User.Identity.Name);
        await http.SignOutAsync(AdminAuth.Scheme);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> MeAsync(HttpContext http, ApimDbContext db, CancellationToken ct)
    {
        var userId = http.User.UserId();
        var user = await db.AdminUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        return user is null ? TypedResults.Unauthorized() : TypedResults.Ok(ToCurrentUser(user));
    }

    /// <summary>Changes the signed-in user's password. Their other sessions end; this one continues.</summary>
    private static async Task<IResult> ChangePasswordAsync(ChangePasswordRequest request, HttpContext http,
        ApimDbContext db, IPasswordHasher<AdminUser> hasher, TimeProvider time, ILogger<AdminUser> logger,
        CancellationToken ct)
    {
        var userId = http.User.UserId();
        var user = await db.AdminUsers.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return TypedResults.Unauthorized();

        var errors = new ValidationErrors();
        if (hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword ?? "")
            == PasswordVerificationResult.Failed)
        {
            errors.Add("currentPassword", "That isn't your current password.");
        }
        AdminAuth.ValidateNewPassword(request.NewPassword, user.Username, errors, "newPassword");
        if (errors.IsValid && request.NewPassword == request.CurrentPassword)
            errors.Add("newPassword", "Choose a password different from the current one.");
        if (!errors.IsValid) return errors.ToResult();

        user.PasswordHash = hasher.HashPassword(user, request.NewPassword!);
        user.SecurityStamp = AdminAuth.NewSecurityStamp();
        user.MustChangePassword = false;
        user.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);

        // Re-issue this session's cookie with the new security stamp, keeping the original sign-in time.
        await http.SignInAsync(AdminAuth.Scheme, AdminAuth.CreatePrincipal(user, http.User.SignedInAt()));
        logger.LogInformation("Admin user {Username} changed their password", user.Username);
        return TypedResults.Ok(ToCurrentUser(user));
    }

    private static CurrentUserResponse ToCurrentUser(AdminUser user) =>
        new(user.Id, user.Username, user.MustChangePassword);

    private static IResult InvalidCredentials() =>
        TypedResults.Problem(title: "Sign-in failed", detail: "The username or password is incorrect.",
            statusCode: StatusCodes.Status401Unauthorized);

    private static IResult Locked(TimeSpan remaining) =>
        TypedResults.Problem(title: "Account locked",
            detail: $"Too many failed sign-ins. Try again in {Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes))} minutes.",
            statusCode: StatusCodes.Status429TooManyRequests);

    private static string Truncate(string value) => value.Length <= 100 ? value : value[..100];
}
