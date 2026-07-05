namespace CartService.Domain.Entities;

/// <summary>
/// Individual line item in a cart. Price is a snapshot from the moment of addition.
/// </summary>
public class CartItem
{
    // Id MUST stay unset (Guid.Empty) on new items: EF's change detection
    // treats a discovered child with a set key as an existing row (UPDATE,
    // not INSERT). EF generates a UUIDv7 client-side when the item is added.
    public Guid Id { get; init; }
    public Guid CartId { get; init; }
    public Guid ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal UnitPrice { get; init; }
    public int Quantity { get; internal set; }
    public DateTimeOffset AddedAt { get; init; } = DateTimeOffset.UtcNow;

    public decimal LineTotal => UnitPrice * Quantity;

    internal void IncreaseQuantity(int amount)
    {
        if (amount <= 0)
            throw new DomainException("invalid_quantity", "Quantity increment must be greater than zero.");
        Quantity += amount;
    }

    internal void SetQuantity(int quantity)
    {
        if (quantity <= 0)
            throw new DomainException("invalid_quantity", "Quantity must be greater than zero.");
        Quantity = quantity;
    }
}
