using System.Globalization;
using System.Security.Claims;
using System.Text.RegularExpressions;
using EnterpriseApim.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EnterpriseApim.AdminApi.Auth;

internal static partial class AdminAuth
{
    public const string Scheme = CookieAuthenticationDefaults.AuthenticationScheme;
    public const string CookieName = "apim_admin";
    public const string SignInRateLimitPolicy = "sign-in";

    /// <summary>
    /// Unsafe requests must carry this header. Browsers can't add custom headers to cross-site requests
    /// without a CORS preflight (which the admin API never allows), so this blocks CSRF.
    /// </summary>
    public const string CsrfHeader = "X-Requested-With";

    public const int MinPasswordLength = 12;
    public const int MaxPasswordLength = 128;

    private const string SecurityStampClaim = "apim:stamp";
    private const string SignedInAtClaim = "apim:signed_in_at";
    private const string MustChangePasswordItem = "apim:must_change_password";

    [GeneratedRegex("^[a-z0-9][a-z0-9._@-]{2,99}$")]
    private static partial Regex UsernamePattern();

    public static string NormalizeUsername(string? username) =>
        (username ?? "").Trim().ToLowerInvariant();

    public static void ValidateUsername(string username, ValidationErrors errors)
    {
        if (!UsernamePattern().IsMatch(username))
            errors.Add("username", "Use 3 to 100 letters, digits, dots, dashes, underscores or @.");
    }

    public static void ValidateNewPassword(string? password, string username, ValidationErrors errors,
        string field = "password")
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
            errors.Add(field, $"Use at least {MinPasswordLength} characters. A few unrelated words work well.");
        else if (password.Length > MaxPasswordLength)
            errors.Add(field, $"Use {MaxPasswordLength} characters or fewer.");
        else if (password.Contains(username, StringComparison.OrdinalIgnoreCase))
            errors.Add(field, "Don't include the username in the password.");
    }

    public static string NewSecurityStamp() => Guid.NewGuid().ToString("N");

    public static ClaimsPrincipal CreatePrincipal(AdminUser user, DateTimeOffset signedInAt) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(SecurityStampClaim, user.SecurityStamp),
            new Claim(SignedInAtClaim, signedInAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture))
        ], Scheme));

    public static Guid UserId(this ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static DateTimeOffset SignedInAt(this ClaimsPrincipal principal) =>
        DateTimeOffset.FromUnixTimeSeconds(long.Parse(principal.FindFirstValue(SignedInAtClaim)!, CultureInfo.InvariantCulture));

    public static IServiceCollection AddAdminAuthentication(this IServiceCollection services, IWebHostEnvironment environment,
        AdminOptions options)
    {
        services.AddAuthentication(Scheme).AddCookie(Scheme, cookie =>
        {
            cookie.Cookie.Name = CookieName;
            cookie.Cookie.HttpOnly = true;
            cookie.Cookie.SameSite = SameSiteMode.Strict;
            // Browsers accept Secure cookies on http://localhost; anywhere else, serve the console over HTTPS.
            cookie.Cookie.SecurePolicy = environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
            cookie.ExpireTimeSpan = TimeSpan.FromMinutes(options.SessionIdleMinutes);
            cookie.SlidingExpiration = true;

            // An API answers with status codes, not redirects to a login page.
            cookie.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            cookie.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
            cookie.Events.OnValidatePrincipal = ValidateSessionAsync;
        });
        services.AddAuthorization();
        return services;
    }

    /// <summary>
    /// Runs on every authenticated request. Ends the session if the user was deleted or disabled, changed
    /// their password elsewhere (security stamp), or signed in longer ago than Admin:SessionMaxHours.
    /// </summary>
    private static async Task ValidateSessionAsync(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal!;
        var services = context.HttpContext.RequestServices;
        var options = services.GetRequiredService<IOptions<AdminOptions>>().Value;
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();

        var userId = principal.UserId();
        var user = await services.GetRequiredService<ApimDbContext>().AdminUsers.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.IsActive, u.SecurityStamp, u.MustChangePassword })
            .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

        var valid = user is { IsActive: true }
            && user.SecurityStamp == principal.FindFirstValue(SecurityStampClaim)
            && now - principal.SignedInAt() < TimeSpan.FromHours(options.SessionMaxHours);

        if (!valid)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(Scheme);
            return;
        }

        if (user!.MustChangePassword)
            context.HttpContext.Items[MustChangePasswordItem] = true;
    }

    /// <summary>Blocks the admin API, apart from the auth endpoints, until a required password change is done.</summary>
    public static TBuilder RequirePasswordChanged<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter((context, next) =>
            context.HttpContext.Items.ContainsKey(MustChangePasswordItem)
                ? ValueTask.FromResult<object?>(TypedResults.Problem(
                    title: "Password change required",
                    detail: "Choose a new password before using the admin console.",
                    statusCode: StatusCodes.Status403Forbidden))
                : next(context));

    public static IApplicationBuilder UseCsrfHeaderCheck(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var method = context.Request.Method;
            var isSafe = HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);
            if (!isSafe && context.Request.Path.StartsWithSegments("/api") &&
                !context.Request.Headers.ContainsKey(CsrfHeader))
            {
                await TypedResults.Problem(
                    title: "Missing request header",
                    detail: $"Requests that change data must include the {CsrfHeader} header.",
                    statusCode: StatusCodes.Status403Forbidden).ExecuteAsync(context);
                return;
            }

            await next(context);
        });
}
