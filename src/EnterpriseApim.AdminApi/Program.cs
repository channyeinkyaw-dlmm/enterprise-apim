using System.Threading.RateLimiting;
using EnterpriseApim.AdminApi;
using EnterpriseApim.AdminApi.Auth;
using EnterpriseApim.AdminApi.Endpoints;
using EnterpriseApim.Core;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

var adminSection = builder.Configuration.GetSection(AdminOptions.Section);
builder.Services.Configure<AdminOptions>(adminSection);
var adminOptions = adminSection.Get<AdminOptions>() ?? new AdminOptions();

builder.Services.AddApimDatabase(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ChangeNotifier>();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

// Session cookies are encrypted with Data Protection keys. Keeping the keys in PostgreSQL means sessions
// survive restarts and work across several admin API instances.
builder.Services.AddDataProtection()
    .SetApplicationName("EnterpriseApim.AdminApi")
    .PersistKeysToDbContext<ApimDbContext>();
builder.Services.AddSingleton<IPasswordHasher<AdminUser>, PasswordHasher<AdminUser>>();
builder.Services.AddAdminAuthentication(builder.Environment, adminOptions);

// Slows password guessing from one address; per-account lockout covers distributed guessing.
builder.Services.AddRateLimiter(o =>
{
    o.OnRejected = (context, _) => new ValueTask(TypedResults.Problem(
        title: "Too many attempts",
        detail: "Too many sign-in attempts from your network. Wait a minute and try again.",
        statusCode: StatusCodes.Status429TooManyRequests).ExecuteAsync(context.HttpContext));
    o.AddPolicy(AdminAuth.SignInRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

await DatabaseInitializer.InitializeAsync(app.Services, app.Configuration, app.Logger);

app.UseExceptionHandler();

// The React admin console is built into wwwroot (see the Dockerfile). index.html is revalidated on every
// load so browsers pick up a new console after a deploy; files in assets/ have content hashes in their
// names, so they can be cached for good.
var consoleFiles = new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        var headers = context.Context.Response.Headers;
        if (context.File.Name == "index.html")
            headers.CacheControl = "no-cache";
        else if (context.Context.Request.Path.StartsWithSegments("/assets"))
            headers.CacheControl = "public, max-age=31536000, immutable";
    }
};
app.UseDefaultFiles();
app.UseStaticFiles(consoleFiles);

app.UseCsrfHeaderCheck();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapGet("/health", () => TypedResults.Ok(new { status = "ok" }));

var api = app.MapGroup("/api");
api.MapAuthEndpoints();
api.MapGroup("").RequireAuthorization().RequirePasswordChanged()
   .MapSystemEndpoints()
   .MapTenantEndpoints()
   .MapApiKeyEndpoints()
   .MapClusterEndpoints()
   .MapRouteEndpoints()
   .MapScopeEndpoints()
   .MapAdminUserEndpoints();
api.MapFallback(() => TypedResults.Problem(title: "Not found", statusCode: StatusCodes.Status404NotFound));

// Client-side routing for the console.
app.MapFallbackToFile("index.html", consoleFiles);

app.Run();
