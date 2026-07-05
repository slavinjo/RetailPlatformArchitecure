using CartService.Domain.Entities;

namespace CartService.Domain.Services;

/// <summary>
/// Domain service for cart operations that require the product catalog.
/// Enforces the add-item rules from the spec (§6.1): product must exist,
/// be available, and quantity must be positive. Price is snapshotted here.
/// </summary>
public sealed class CartOperations
{
    private readonly IProductCatalogReader _catalog;

    public CartOperations(IProductCatalogReader catalog)
    {
        _catalog = catalog;
    }

    public async Task AddItemAsync(Cart cart, Guid productId, int quantity, CancellationToken ct = default)
    {
        var product = await _catalog.GetProductAsync(productId, ct);

        if (product is null)
            throw new DomainException("product_not_found", $"Product '{productId}' not found.");

        if (!product.IsAvailable)
            throw new DomainException("product_unavailable", $"Product '{productId}' is not available for purchase.");

        if (quantity <= 0)
            throw new DomainException("invalid_quantity", "Quantity must be greater than zero.");

        // Snapshot: name and price are taken from the catalog at the moment of addition
        cart.AddItem(product.Id, product.Name, product.UnitPrice, quantity);
    }
}
