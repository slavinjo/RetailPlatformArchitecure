using CartService.Domain;
using CartService.Infrastructure.Catalog;
using CartService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CartService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");

        services.AddDbContext<CartDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<IProductCatalogReader, PostgresProductCatalogReader>();

        return services;
    }
}
