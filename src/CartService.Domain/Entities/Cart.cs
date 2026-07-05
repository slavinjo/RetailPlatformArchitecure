using System.Collections.ObjectModel;

namespace CartService.Domain.Entities;

/// <summary>
/// Cart aggregate root. Encapsulates all business rules for cart item management.
/// </summary>
public class Cart
{
    public Guid Id { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Owning subject (the token 'sub'), or null for an anonymous/guest cart.
    /// The edge (BFF) tracks guest carts by id; a logged-in user's cart is
    /// resolved by this owner — the shared source of truth across web/mobile.
    /// </summary>
    public Guid? OwnerId { get; private set; }

    private readonly List<CartItem> _items = new();
    public IReadOnlyCollection<CartItem> Items => new ReadOnlyCollection<CartItem>(_items);

    public Cart()
    {
        // UUIDv7 (time-ordered) keeps B-tree inserts near the right edge of the
        // index, avoiding the page splits random v4 GUIDs cause in Postgres.
        Id = Guid.CreateVersion7();
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>
    /// Bind this cart to an authenticated user (the token 'sub'). A cart starts
    /// anonymous; once owned it cannot be reassigned to a different subject.
    /// Idempotent when called again with the same owner (e.g. guest claim).
    /// </summary>
    public void AssignToOwner(Guid ownerId)
    {
        if (OwnerId is not null && OwnerId != ownerId)
            throw new DomainException("cart_forbidden", "Cart is owned by another user.");

        OwnerId = ownerId;
    }

    /// <summary>
    /// Add an item or merge with existing line (quantity increment, price NOT re-snapped).
    /// </summary>
    public void AddItem(Guid productId, string productName, decimal unitPrice, int quantity)
    {
        if (quantity <= 0)
            throw new DomainException("invalid_quantity", "Quantity must be greater than zero.");

        var existing = _items.FirstOrDefault(i => i.ProductId == productId);
        if (existing is not null)
        {
            existing.IncreaseQuantity(quantity);
        }
        else
        {
            _items.Add(new CartItem
            {
                CartId = Id,
                ProductId = productId,
                ProductName = productName,
                UnitPrice = unitPrice,
                Quantity = quantity
            });
        }

        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Set absolute quantity for an existing line.
    /// </summary>
    public void UpdateItemQuantity(Guid productId, int quantity)
    {
        if (quantity <= 0)
            throw new DomainException("invalid_quantity", "Quantity must be greater than zero.");

        var item = _items.FirstOrDefault(i => i.ProductId == productId);
        if (item is null)
            throw new DomainException("cart_item_not_found", $"Cart item for product '{productId}' not found.");

        item.SetQuantity(quantity);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Remove a specific item from the cart.
    /// </summary>
    public void RemoveItem(Guid productId)
    {
        var item = _items.FirstOrDefault(i => i.ProductId == productId);
        if (item is null)
            throw new DomainException("cart_item_not_found", $"Cart item for product '{productId}' not found.");

        _items.Remove(item);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Clear all items from the cart.
    /// </summary>
    public void Clear()
    {
        _items.Clear();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public int TotalItems => _items.Sum(i => i.Quantity);
    public decimal TotalAmount => _items.Sum(i => i.LineTotal);
}
