using CartService.Domain;
using CartService.Domain.Entities;
using CartService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CartService.Infrastructure.Catalog;

/// <summary>
/// Production implementation: reads products from the local PostgreSQL database.
/// </summary>
public class PostgresProductCatalogReader : IProductCatalogReader
{
    private readonly CartDbContext _context;

    public PostgresProductCatalogReader(CartDbContext context)
    {
        _context = context;
    }

    public async Task<ProductInfo?> GetProductAsync(Guid productId, CancellationToken ct = default)
    {
        var product = await _context.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productId, ct);

        return product is null ? null : new ProductInfo(product.Id, product.Name, product.UnitPrice, product.IsAvailable);
    }
}
