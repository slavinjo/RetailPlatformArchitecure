namespace CartService.Domain.Dtos;

/// <summary>
/// Response DTO for cart endpoints.
/// </summary>
public sealed class CartResponse
{
    public Guid CartId { get; init; }
    public List<CartItemResponse> Items { get; init; } = new();
    public int TotalItems { get; init; }
    public decimal TotalAmount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed class CartItemResponse
{
    public Guid ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal UnitPrice { get; init; }
    public int Quantity { get; init; }
    public decimal LineTotal { get; init; }
}
