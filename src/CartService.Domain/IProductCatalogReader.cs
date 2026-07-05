namespace CartService.Domain;

/// <summary>
/// Narrow read-only abstraction for product catalog.
/// Allows swapping the data source (DB, HTTP client, in-memory) without changing cart logic.
/// </summary>
public interface IProductCatalogReader
{
    Task<ProductInfo?> GetProductAsync(Guid productId, CancellationToken ct = default);
}

/// <summary>
/// DTO returned by the catalog reader — immutable, minimal surface.
/// </summary>
public sealed record ProductInfo(Guid Id, string Name, decimal UnitPrice, bool IsAvailable);
