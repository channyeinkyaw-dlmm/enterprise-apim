using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EnterpriseApim.Core;

public static class ServiceCollectionExtensions
{
    public const string ConnectionStringName = "Apim";

    public static IServiceCollection AddApimDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = GetApimConnectionString(configuration);
        services.AddDbContext<ApimDbContext>(o => o.UseNpgsql(connectionString));
        return services;
    }

    /// <summary>
    /// The configured connection string, with Kerberos (GSS) encryption off unless it sets it explicitly.
    /// Npgsql tries GSS encryption first by default, which needs libgssapi_krb5; the .NET container images
    /// don't include it, so every new connection would log "Cannot load library libgssapi_krb5.so.2".
    /// </summary>
    public static string GetApimConnectionString(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string 'ConnectionStrings:{ConnectionStringName}' is not configured.");

        var given = new DbConnectionStringBuilder { ConnectionString = connectionString };
        var setsGssEncryption = given.Keys.Cast<string>().Any(key =>
            key.Replace(" ", "").Equals("GssEncryptionMode", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("gssencmode", StringComparison.OrdinalIgnoreCase));
        if (setsGssEncryption) return connectionString;

        return new NpgsqlConnectionStringBuilder(connectionString) { GssEncryptionMode = GssEncryptionMode.Disable }
            .ConnectionString;
    }
}
