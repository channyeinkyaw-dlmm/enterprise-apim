using System.Text;
using EnterpriseApim.AdminApi.Auth;
using EnterpriseApim.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseApim.AdminApi;

/// <summary>
/// Command-line tasks run instead of the web server, for example
/// <c>dotnet EnterpriseApim.AdminApi.dll create-admin alice</c>. They use the same configuration
/// (connection string) as the admin API.
/// </summary>
public static class AdminCommands
{
    public const string CreateAdmin = "create-admin";
    private const string ResetOption = "--reset";

    public static bool IsCommand(string[] args) => args.Length > 0 && args[0] == CreateAdmin;

    /// <summary>
    /// Creates an admin user, or with --reset sets a new password for an existing one and unlocks and enables it.
    /// The password is read from a hidden prompt, or from standard input when it's redirected, never from arguments.
    /// </summary>
    public static async Task<int> RunAsync(string[] args)
    {
        var rest = args.Skip(1).ToArray();
        var reset = rest.Contains(ResetOption);
        var positional = rest.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
        if (positional.Length != 1 || rest.Any(a => a.StartsWith("--", StringComparison.Ordinal) && a != ResetOption))
        {
            Console.Error.WriteLine($"""
                Usage: {CreateAdmin} <username> [{ResetOption}]

                  Creates an admin console user. You're asked for the password; to script it, pipe it on
                  standard input instead.
                  {ResetOption}   The user exists: set a new password, unlock and enable it.
                """);
            return 2;
        }

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        // On a new database EF logs a failed query for the migration history table before creating it.
        // That's expected; real database errors still surface as exceptions.
        builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.None);
        builder.Services.AddApimDatabase(builder.Configuration);
        builder.Services.AddSingleton<IPasswordHasher<AdminUser>, PasswordHasher<AdminUser>>();
        await using var app = builder.Build();

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApimDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AdminUser>>();
        await DatabaseInitializer.MigrateAsync(db, app.Logger);

        var username = AdminAuth.NormalizeUsername(positional[0]);
        var errors = new ValidationErrors();
        AdminAuth.ValidateUsername(username, errors);
        if (!errors.IsValid) return Fail(errors);

        var user = await db.AdminUsers.FirstOrDefaultAsync(u => u.Username == username);
        if (user is not null && !reset)
            return Fail($"Admin user \"{username}\" already exists. Add {ResetOption} to set a new password, unlock and enable it.");
        if (user is null && reset)
            return Fail($"There is no admin user \"{username}\" to reset. Leave out {ResetOption} to create it.");

        var password = ReadPassword(username);
        if (password is null) return 1;
        AdminAuth.ValidateNewPassword(password, username, errors);
        if (!errors.IsValid) return Fail(errors);

        var now = DateTimeOffset.UtcNow;
        if (user is null)
        {
            user = new AdminUser { Id = Guid.CreateVersion7(), Username = username, CreatedAt = now };
            db.AdminUsers.Add(user);
        }

        user.PasswordHash = hasher.HashPassword(user, password);
        user.SecurityStamp = AdminAuth.NewSecurityStamp(); // ends any existing sessions
        user.IsActive = true;
        user.MustChangePassword = false;
        user.FailedSignInCount = 0;
        user.LockedUntil = null;
        user.UpdatedAt = now;
        await db.SaveChangesAsync();

        Console.WriteLine(reset
            ? $"Set a new password for admin user \"{username}\", and unlocked and enabled it."
            : $"Created admin user \"{username}\". Sign in to the admin console with it.");
        return 0;
    }

    private static string? ReadPassword(string username)
    {
        if (Console.IsInputRedirected)
            return Console.In.ReadLine();

        var password = ReadHidden($"Password for {username}: ");
        if (ReadHidden("Repeat the password: ") == password) return password;

        Console.Error.WriteLine("The passwords don't match. Nothing was changed.");
        return null;
    }

    private static string ReadHidden(string prompt)
    {
        Console.Write(prompt);
        var text = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace)
            {
                if (text.Length > 0) text.Length--;
            }
            else if (!char.IsControl(key.KeyChar))
            {
                text.Append(key.KeyChar);
            }
        }
        Console.WriteLine();
        return text.ToString();
    }

    private static int Fail(ValidationErrors errors) =>
        Fail(string.Join(" ", errors.All.Select(e => e.Message)) + " Nothing was changed.");

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}
