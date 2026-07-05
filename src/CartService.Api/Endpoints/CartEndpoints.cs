using System.Security.Claims;
using CartService.Domain.Dtos;
using CartService.Domain.Entities;
using CartService.Domain.Services;
using CartService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CartService.Api.Endpoints;

/// <summary>
/// Minimal API endpoints for cart management, grouped under /carts.
/// Domain rule violations (DomainException) bubble up to the global
/// exception handler, which maps them to RFC 7807 Problem Details (§8).
/// </summary>
public static class CartEndpoints
{
    public static void MapCartEndpoints(this IEndpointRouteBuilder app, bool requireAuth = false)
    {
        var group = app.MapGroup("/carts").WithTags("Carts");

        // 1. POST /carts — Create empty cart
        var create = group.MapPost("/", CreateCart)
            .WithName("CreateCart")
            .Produces<CartResponse>(StatusCodes.Status201Created);

        // 2. GET /carts/{cartId} — Get cart with items
        var get = group.MapGet("/{cartId:guid}", GetCart)
            .WithName("GetCart")
            .Produces<CartResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        // 3. POST /carts/{cartId}/items — Add item (or increase quantity)
        var addItem = group.MapPost("/{cartId:guid}/items", AddItem)
            .WithName("AddCartItem")
            .Produces<CartResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // 4. PUT /carts/{cartId}/items/{productId} — Set absolute quantity
        var updateItem = group.MapPut("/{cartId:guid}/items/{productId:guid}", UpdateItemQuantity)
            .WithName("UpdateCartItemQuantity")
            .Produces<CartResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // 5. DELETE /carts/{cartId}/items/{productId} — Remove item
        var removeItem = group.MapDelete("/{cartId:guid}/items/{productId:guid}", RemoveItem)
            .WithName("RemoveCartItem")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // 6. DELETE /carts/{cartId}/items — Clear cart
        var clearCart = group.MapDelete("/{cartId:guid}/items", ClearCart)
            .WithName("ClearCart")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Policy-based authorization (§6.5): scope controls endpoint access.
        // Applied only when the Auth:Enabled feature flag is on.
        if (requireAuth)
        {
            get.RequireAuthorization("CartRead");
            create.RequireAuthorization("CartWrite");
            addItem.RequireAuthorization("CartWrite");
            updateItem.RequireAuthorization("CartWrite");
            removeItem.RequireAuthorization("CartWrite");
            clearCart.RequireAuthorization("CartWrite");
        }
    }

    private static async Task<IResult> CreateCart(CartDbContext context, ClaimsPrincipal user, CancellationToken ct)
    {
        var cart = new Cart();

        // When auth is on, bind the new cart to the caller; otherwise it stays a guest cart.
        if (TryGetSub(user) is Guid ownerId)
            cart.AssignToOwner(ownerId);

        context.Carts.Add(cart);
        await context.SaveChangesAsync(ct);
        return Results.Created($"/carts/{cart.Id}", ToResponse(cart));
    }

    private static async Task<IResult> GetCart(Guid cartId, CartDbContext context, ClaimsPrincipal user, CancellationToken ct)
    {
        var cart = await context.Carts
            .AsNoTracking()
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.Id == cartId, ct);

        if (cart is null)
            return CartNotFound();

        return OwnershipGuard(cart, user) ?? Results.Ok(ToResponse(cart));
    }

    private static async Task<IResult> AddItem(Guid cartId, [FromBody] AddItemRequest request, CartDbContext context, CartOperations operations, ClaimsPrincipal user, CancellationToken ct)
    {
        var cart = await context.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.Id == cartId, ct);

        if (cart is null)
            return CartNotFound();

        if (OwnershipGuard(cart, user) is { } forbidden)
            return forbidden;

        await operations.AddItemAsync(cart, request.ProductId, request.Quantity, ct);

        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyConflict();
        }

        return Results.Ok(ToResponse(cart));
    }

    private static async Task<IResult> UpdateItemQuantity(Guid cartId, Guid productId, [FromBody] UpdateQuantityRequest request, CartDbContext context, ClaimsPrincipal user, CancellationToken ct)
    {
        var cart = await context.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.Id == cartId, ct);

        if (cart is null)
            return CartNotFound();

        if (OwnershipGuard(cart, user) is { } forbidden)
            return forbidden;

        cart.UpdateItemQuantity(productId, request.Quantity);

        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyConflict();
        }

        return Results.Ok(ToResponse(cart));
    }

    private static async Task<IResult> RemoveItem(Guid cartId, Guid productId, CartDbContext context, ClaimsPrincipal user, CancellationToken ct)
    {
        var cart = await context.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.Id == cartId, ct);

        if (cart is null)
            return CartNotFound();

        if (OwnershipGuard(cart, user) is { } forbidden)
            return forbidden;

        cart.RemoveItem(productId);

        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyConflict();
        }

        return Results.NoContent();
    }

    private static async Task<IResult> ClearCart(Guid cartId, CartDbContext context, ClaimsPrincipal user, CancellationToken ct)
    {
        var cart = await context.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.Id == cartId, ct);

        if (cart is null)
            return CartNotFound();

        if (OwnershipGuard(cart, user) is { } forbidden)
            return forbidden;

        cart.Clear();

        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyConflict();
        }

        return Results.NoContent();
    }

    #region Helpers

    private static CartResponse ToResponse(Cart cart)
    {
        return new CartResponse
        {
            CartId = cart.Id,
            Items = cart.Items.Select(i => new CartItemResponse
            {
                ProductId = i.ProductId,
                ProductName = i.ProductName,
                UnitPrice = i.UnitPrice,
                Quantity = i.Quantity,
                LineTotal = i.LineTotal
            }).ToList(),
            TotalItems = cart.TotalItems,
            TotalAmount = cart.TotalAmount,
            CreatedAt = cart.CreatedAt,
            UpdatedAt = cart.UpdatedAt
        };
    }

    // The token subject ('sub'), or null when unauthenticated (auth off / no token).
    private static Guid? TryGetSub(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue("sub"), out var sub) ? sub : null;

    // Ownership check (§6.5): scope grants endpoint access, but a cart bound to
    // an owner may only be touched by that subject. Guest carts (OwnerId null)
    // are guarded only by their unguessable id. No-op when auth is off, since
    // then no cart has an owner. Returns a 403 result, or null when access is ok.
    private static IResult? OwnershipGuard(Cart cart, ClaimsPrincipal user) =>
        cart.OwnerId is Guid owner && TryGetSub(user) != owner ? Forbidden() : null;

    // Results.Problem produces application/problem+json (RFC 7807), unlike
    // Results.NotFound(object) which would serialize as plain application/json.
    private static IResult CartNotFound() =>
        Results.Problem(
            type: "https://cartservice/errors/cart_not_found",
            title: "Cart not found",
            detail: "Cart not found.",
            statusCode: StatusCodes.Status404NotFound);

    private static IResult Forbidden() =>
        Results.Problem(
            type: "https://cartservice/errors/cart_forbidden",
            title: "Forbidden",
            detail: "You do not have access to this cart.",
            statusCode: StatusCodes.Status403Forbidden);

    private static IResult ConcurrencyConflict() =>
        Results.Problem(
            type: "https://cartservice/errors/concurrency_conflict",
            title: "Concurrency conflict",
            detail: "Cart was modified by another operation. Please retry.",
            statusCode: StatusCodes.Status409Conflict);

    #endregion
}
