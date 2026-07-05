namespace CartService.Domain.Dtos;

/// <summary>
/// Request DTO for adding an item to a cart.
/// </summary>
public sealed class AddItemRequest
{
    public Guid ProductId { get; init; }
    public int Quantity { get; init; }
}

/// <summary>
/// Request DTO for updating item quantity.
/// </summary>
public sealed class UpdateQuantityRequest
{
    public int Quantity { get; init; }
}
