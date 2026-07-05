namespace CartService.Domain.Entities;

/// <summary>
/// Product entity (stored in DB, read-only from cart service perspective).
/// Seeded on startup; cart service reads but does not manage products.
/// </summary>
public class Product
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public decimal UnitPrice { get; init; }
    public bool IsAvailable { get; init; }
}
