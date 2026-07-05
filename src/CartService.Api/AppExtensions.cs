using CartService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CartService.Api;

/// <summary>
/// App-level extensions for database initialization.
/// </summary>
public static class AppExtensions
{
    public static async Task EnsureDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CartDbContext>();

        // Apply migrations
        await context.Database.MigrateAsync();

        // Seed products
        await DatabaseSeeder.SeedAsync(context);
    }
}
