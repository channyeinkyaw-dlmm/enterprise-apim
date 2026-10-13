using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EnterpriseApim.Core;

public static class ServiceCollectionExtensions
{
    public const string ConnectionStringName = "Apim";

    public static IServiceCollection AddApimDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string 'ConnectionStrings:{ConnectionStringName}' is not configured.");

        services.AddDbContext<ApimDbContext>(o => o.UseNpgsql(connectionString));
        return services;
    }
}
