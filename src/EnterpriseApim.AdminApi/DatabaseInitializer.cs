using System.Reflection;
using EnterpriseApim.AdminApi.Auth;
using EnterpriseApim.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EnterpriseApim.AdminApi;

public static class DatabaseInitializer
{
    /// <summary>
    /// Applies pending EF Core migrations, creates the first admin user if there is none, and optionally seeds demo routes.
    /// </summary>
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration, ILogger logger)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApimDbContext>();

        const int maxAttempts = 30;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await BaselineEnsureCreatedSchemaAsync(db, logger);
                await db.Database.MigrateAsync();
                break;
            }
            catch (NpgsqlException ex) when (ex.IsTransient && attempt < maxAttempts)
            {
                logger.LogWarning("Database not reachable yet (attempt {Attempt}/{Max}): {Message}",
                    attempt, maxAttempts, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }

        await EnsureAdminUserAsync(db, scope.ServiceProvider, configuration, logger);

        if (configuration.GetValue<bool>("Apim:SeedDemo"))
            await SeedDemoAsync(db, configuration, logger);
    }

    /// <summary>Creates the bootstrap admin from Admin:BootstrapUsername/Password when no admin user exists.</summary>
    private static async Task EnsureAdminUserAsync(ApimDbContext db, IServiceProvider services,
        IConfiguration configuration, ILogger logger)
    {
        if (await db.AdminUsers.AnyAsync()) return;

        var options = configuration.GetSection(AdminOptions.Section).Get<AdminOptions>() ?? new AdminOptions();
        var username = AdminAuth.NormalizeUsername(options.BootstrapUsername);
        var errors = new ValidationErrors();
        AdminAuth.ValidateUsername(username, errors);
        AdminAuth.ValidateNewPassword(options.BootstrapPassword, username, errors);
        if (!errors.IsValid)
        {
            throw new InvalidOperationException(
                "There are no admin users yet. Set Admin__BootstrapUsername and Admin__BootstrapPassword " +
                $"(at least {AdminAuth.MinPasswordLength} characters, not containing the username) " +
                "to create the first one.");
        }

        var now = DateTimeOffset.UtcNow;
        var user = new AdminUser
        {
            Id = Guid.CreateVersion7(),
            Username = username,
            SecurityStamp = AdminAuth.NewSecurityStamp(),
            MustChangePassword = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        user.PasswordHash = services.GetRequiredService<IPasswordHasher<AdminUser>>()
            .HashPassword(user, options.BootstrapPassword);
        db.AdminUsers.Add(user);
        await db.SaveChangesAsync();
        logger.LogInformation(
            "Created admin user {Username}. Sign in and choose a new password, then remove the bootstrap settings",
            username);
    }

    /// <summary>
    /// Databases created before migrations (with EnsureCreated) already have the InitialCreate schema
    /// but no migration history. Record InitialCreate as applied so MigrateAsync doesn't recreate the tables.
    /// </summary>
    private static async Task BaselineEnsureCreatedSchemaAsync(ApimDbContext db, ILogger logger)
    {
        if (!await db.Database.CanConnectAsync()) return; // MigrateAsync creates the database

        var hasHistory = await db.Database
            .SqlQuery<bool>($"SELECT to_regclass('public.\"__EFMigrationsHistory\"') IS NOT NULL AS \"Value\"")
            .SingleAsync();
        var hasSchema = await db.Database
            .SqlQuery<bool>($"SELECT to_regclass('public.tenants') IS NOT NULL AS \"Value\"")
            .SingleAsync();
        if (hasHistory || !hasSchema) return;

        var initialMigration = db.Database.GetMigrations().First();
        var efVersion = typeof(DbContext).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

        await db.Database.ExecuteSqlAsync($"""
            CREATE TABLE "__EFMigrationsHistory" (
                "MigrationId" character varying(150) NOT NULL PRIMARY KEY,
                "ProductVersion" character varying(32) NOT NULL);
            INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion") VALUES ({initialMigration}, {efVersion});
            """);
        logger.LogInformation("Existing schema found without migration history; marked {Migration} as applied",
            initialMigration);
    }

    private static async Task SeedDemoAsync(ApimDbContext db, IConfiguration configuration, ILogger logger)
    {
        if (await db.Clusters.AnyAsync()) return;

        var now = DateTimeOffset.UtcNow;
        var backendUrl = configuration["Apim:DemoBackendUrl"] ?? "http://localhost:5100/";

        db.Clusters.Add(new GatewayCluster
        {
            Id = "sample-backend",
            LoadBalancingPolicy = "RoundRobin",
            HealthCheckEnabled = true,
            HealthCheckPath = "/health",
            HealthCheckIntervalSeconds = 15,
            UpdatedAt = now,
            Destinations =
            [
                new ClusterDestination
                {
                    Id = Guid.CreateVersion7(),
                    ClusterId = "sample-backend",
                    Name = "primary",
                    Address = backendUrl
                }
            ]
        });

        if (!await db.Scopes.AnyAsync(s => s.Name == "orders.read"))
        {
            db.Scopes.Add(new Scope
            {
                Name = "orders.read",
                Description = "Read orders.",
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        db.Routes.AddRange(
            new GatewayRoute
            {
                Id = "echo",
                ClusterId = "sample-backend",
                Path = "/api/echo/{**catch-all}",
                TransformsJson = """[{"PathRemovePrefix":"/api"}]""",
                Description = "Any registered client. Echoes what the backend receives.",
                UpdatedAt = now
            },
            new GatewayRoute
            {
                Id = "orders",
                ClusterId = "sample-backend",
                Path = "/api/orders/{**catch-all}",
                RequiredScope = "orders.read",
                TransformsJson = """[{"PathRemovePrefix":"/api"}]""",
                Description = "Only clients whose key has the orders.read scope.",
                UpdatedAt = now
            },
            new GatewayRoute
            {
                Id = "public-status",
                ClusterId = "sample-backend",
                Path = "/public/status",
                AllowAnonymous = true,
                Description = "Public. No client key needed.",
                UpdatedAt = now
            });

        if (!await db.Tenants.AnyAsync())
        {
            db.Tenants.Add(new Tenant
            {
                Id = Guid.CreateVersion7(),
                Name = "Contoso Ltd",
                Description = "Demo client. Issue it a key from the admin console.",
                RateLimitPerMinute = 600,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Seeded demo backend, routes and client");
    }
}
