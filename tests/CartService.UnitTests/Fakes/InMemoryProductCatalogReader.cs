using CartService.Domain;

namespace CartService.UnitTests.Fakes;

/// <summary>
/// In-memory fake implementation of IProductCatalogReader for unit tests.
/// </summary>
public class InMemoryProductCatalogReader : IProductCatalogReader
{
    private readonly Dictionary<Guid, ProductInfo> _products = new();

    public void Add(ProductInfo product) => _products[product.Id] = product;
    public void Clear() => _products.Clear();

    public Task<ProductInfo?> GetProductAsync(Guid productId, CancellationToken ct = default)
    {
        _products.TryGetValue(productId, out var product);
        return Task.FromResult(product);
    }
}
