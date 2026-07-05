using CartService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CartService.Infrastructure.Persistence;

/// <summary>
/// Seeds sample products on startup if the products table is empty.
/// Ensures a deterministic demo state.
/// </summary>
public static class DatabaseSeeder
{
    public static async Task SeedAsync(CartDbContext context, CancellationToken ct = default)
    {
        if (await context.Products.AnyAsync(ct))
            return; // Already seeded

        var products = new List<Product>
        {
            new() { Id = Guid.Parse("ca23a19d-7a8b-4e5f-9c1d-000000000001"), Name = "Bečka kobasica", UnitPrice = 12.50m, IsAvailable = true },
            new() { Id = Guid.Parse("ca23a19d-7a8b-4e5f-9c1d-000000000002"), Name = "Kruh cjeloviti", UnitPrice = 3.90m, IsAvailable = true },
            new() { Id = Guid.Parse("ca23a19d-7a8b-4e5f-9c1d-000000000003"), Name = "Mlijeko 3.5%", UnitPrice = 2.45m, IsAvailable = true },
            new() { Id = Guid.Parse("ca23a19d-7a8b-4e5f-9c1d-000000000004"), Name = "Sir kajmak", UnitPrice = 15.99m, IsAvailable = true },
            new() { Id = Guid.Parse("ca23a19d-7a8b-4e5f-9c1d-000000000005"), Name = "Pivo 0.5L", UnitPrice = 4.50m, IsAvailable = false },
        };

        await context.Products.AddRangeAsync(products, ct);
        await context.SaveChangesAsync(ct);
    }
}
