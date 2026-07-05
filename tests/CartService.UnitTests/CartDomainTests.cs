using CartService.Domain;
using CartService.Domain.Entities;
using CartService.Domain.Services;
using CartService.UnitTests.Fakes;

namespace CartService.UnitTests;

/// <summary>
/// Unit tests for Cart domain rules.
/// Uses in-memory fake catalog — no database involved.
/// Naming: Method_Scenario_ExpectedResult
/// </summary>
[TestClass]
public class CartDomainTests
{
    private InMemoryProductCatalogReader _catalog = null!;
    private Guid _availableProductId;
    private Guid _unavailableProductId;

    [TestInitialize]
    public void Setup()
    {
        _catalog = new InMemoryProductCatalogReader();
        _availableProductId = Guid.Parse("ca23a19d-7a8b-4e5f-9c1d-000000000001");
        _unavailableProductId = Guid.Parse("ca23a19d-7a8b-4e5f-9c1d-000000000005");

        _catalog.Add(new ProductInfo(_availableProductId, "Bečka kobasica", 12.50m, true));
        _catalog.Add(new ProductInfo(_unavailableProductId, "Pivo 0.5L", 4.50m, false));
    }

    #region Cart Creation

    [TestMethod]
    public void CreateCart_WhenNew_HasValidIdAndTimestamps()
    {
        // Act
        var cart = new Cart();

        // Assert
        cart.Id.Should().NotBe(Guid.Empty);
        cart.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        cart.Items.Should().BeEmpty();
        cart.TotalItems.Should().Be(0);
        cart.TotalAmount.Should().Be(0m);
    }

    #endregion

    #region AddItem

    [TestMethod]
    public void AddItem_WhenProductAvailable_Succeeds()
    {
        // Arrange
        var cart = new Cart();
        var product = _catalog.GetProductAsync(_availableProductId).Result!;

        // Act
        cart.AddItem(product.Id, product.Name, product.UnitPrice, 2);

        // Assert
        cart.Items.Should().HaveCount(1);
        cart.Items.First().ProductId.Should().Be(_availableProductId);
        cart.Items.First().Quantity.Should().Be(2);
        cart.Items.First().UnitPrice.Should().Be(12.50m);
        cart.TotalItems.Should().Be(2);
        cart.TotalAmount.Should().Be(25.00m);
    }

    [TestMethod]
    public async Task AddItem_WhenProductNotFound_ThrowsDomainException()
    {
        // Arrange
        var operations = new CartOperations(_catalog);
        var cart = new Cart();

        // Act & Assert
        var act = () => operations.AddItemAsync(cart, Guid.NewGuid(), 1);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.ErrorCode.Should().Be("product_not_found");
        cart.Items.Should().BeEmpty();
    }

    [TestMethod]
    public async Task AddItem_WhenProductUnavailable_ThrowsDomainException()
    {
        // Arrange
        var operations = new CartOperations(_catalog);
        var cart = new Cart();

        // Act & Assert
        var act = () => operations.AddItemAsync(cart, _unavailableProductId, 1);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.ErrorCode.Should().Be("product_unavailable");
        cart.Items.Should().BeEmpty();
    }

    [TestMethod]
    public void AddItem_WhenQuantityZero_ThrowsDomainException()
    {
        // Arrange
        var cart = new Cart();

        // Act & Assert
        var act = () => cart.AddItem(_availableProductId, "Test", 10m, 0);
        act.Should().Throw<DomainException>()
           .Which.ErrorCode.Should().Be("invalid_quantity");
    }

    [TestMethod]
    public void AddItem_WhenQuantityNegative_ThrowsDomainException()
    {
        // Arrange
        var cart = new Cart();

        // Act & Assert
        var act = () => cart.AddItem(_availableProductId, "Test", 10m, -1);
        act.Should().Throw<DomainException>()
           .Which.ErrorCode.Should().Be("invalid_quantity");
    }

    [TestMethod]
    public void AddItem_WhenSameProductAddedTwice_MergesQuantity()
    {
        // Arrange
        var cart = new Cart();
        var product = _catalog.GetProductAsync(_availableProductId).Result!;

        // Act
        cart.AddItem(product.Id, product.Name, product.UnitPrice, 2);
        cart.AddItem(product.Id, product.Name, product.UnitPrice, 3);

        // Assert
        cart.Items.Should().HaveCount(1);
        cart.Items.First().Quantity.Should().Be(5);
        cart.TotalItems.Should().Be(5);
        cart.TotalAmount.Should().Be(62.50m); // 12.50 * 5
    }

    [TestMethod]
    public async Task AddItem_WhenPriceChangedInCatalog_SnapshotPriceKept()
    {
        // Arrange — snapshot price rule (§6.3)
        var operations = new CartOperations(_catalog);
        var cart = new Cart();

        // Act — add at original price, then the catalog price changes
        await operations.AddItemAsync(cart, _availableProductId, 2);
        _catalog.Add(new ProductInfo(_availableProductId, "Bečka kobasica", 15.00m, true));
        await operations.AddItemAsync(cart, _availableProductId, 1);

        // Assert — price is NOT re-snapped on merge; quantity merged
        cart.Items.Should().HaveCount(1);
        cart.Items.First().Quantity.Should().Be(3);
        // Original snapshot price is kept (12.50), total = 12.50 * 3 = 37.50
        cart.Items.First().UnitPrice.Should().Be(12.50m);
        cart.TotalAmount.Should().Be(37.50m);
    }

    #endregion

    #region UpdateItemQuantity

    [TestMethod]
    public void UpdateItemQuantity_WhenItemExists_SetsAbsoluteQuantity()
    {
        // Arrange
        var cart = new Cart();
        cart.AddItem(_availableProductId, "Bečka kobasica", 12.50m, 2);

        // Act
        cart.UpdateItemQuantity(_availableProductId, 5);

        // Assert
        cart.Items.First().Quantity.Should().Be(5);
        cart.TotalItems.Should().Be(5);
        cart.TotalAmount.Should().Be(62.50m);
    }

    [TestMethod]
    public void UpdateItemQuantity_WhenItemNotFound_ThrowsDomainException()
    {
        // Arrange
        var cart = new Cart();

        // Act & Assert
        var act = () => cart.UpdateItemQuantity(Guid.NewGuid(), 3);
        act.Should().Throw<DomainException>()
           .Which.ErrorCode.Should().Be("cart_item_not_found");
    }

    [TestMethod]
    public void UpdateItemQuantity_WhenQuantityZero_ThrowsDomainException()
    {
        // Arrange
        var cart = new Cart();
        cart.AddItem(_availableProductId, "Bečka kobasica", 12.50m, 2);

        // Act & Assert
        var act = () => cart.UpdateItemQuantity(_availableProductId, 0);
        act.Should().Throw<DomainException>()
           .Which.ErrorCode.Should().Be("invalid_quantity");
    }

    #endregion

    #region RemoveItem

    [TestMethod]
    public void RemoveItem_WhenItemExists_RemovesItem()
    {
        // Arrange
        var cart = new Cart();
        cart.AddItem(_availableProductId, "Bečka kobasica", 12.50m, 2);

        // Act
        cart.RemoveItem(_availableProductId);

        // Assert
        cart.Items.Should().BeEmpty();
        cart.TotalItems.Should().Be(0);
        cart.TotalAmount.Should().Be(0m);
    }

    [TestMethod]
    public void RemoveItem_WhenItemNotFound_ThrowsDomainException()
    {
        // Arrange
        var cart = new Cart();

        // Act & Assert
        var act = () => cart.RemoveItem(Guid.NewGuid());
        act.Should().Throw<DomainException>()
           .Which.ErrorCode.Should().Be("cart_item_not_found");
    }

    #endregion

    #region ClearCart

    [TestMethod]
    public void Clear_WhenCartHasItems_RemovesAllItems()
    {
        // Arrange
        var cart = new Cart();
        cart.AddItem(_availableProductId, "Bečka kobasica", 12.50m, 2);
        var productId2 = Guid.Parse("ca23a19d-7a8b-4e5f-9c1d-000000000002");
        cart.AddItem(productId2, "Kruh cjeloviti", 3.90m, 1);

        // Act
        cart.Clear();

        // Assert
        cart.Items.Should().BeEmpty();
        cart.TotalItems.Should().Be(0);
        cart.TotalAmount.Should().Be(0m);
    }

    #endregion

    #region Ownership

    [TestMethod]
    public void AssignToOwner_WhenCartIsGuest_SetsOwner()
    {
        // Arrange
        var cart = new Cart();
        var owner = Guid.NewGuid();

        // Act
        cart.AssignToOwner(owner);

        // Assert
        cart.OwnerId.Should().Be(owner);
    }

    [TestMethod]
    public void AssignToOwner_WhenSameOwnerAgain_IsIdempotent()
    {
        // Arrange
        var cart = new Cart();
        var owner = Guid.NewGuid();
        cart.AssignToOwner(owner);

        // Act & Assert — re-claiming by the same subject is allowed
        var act = () => cart.AssignToOwner(owner);
        act.Should().NotThrow();
        cart.OwnerId.Should().Be(owner);
    }

    [TestMethod]
    public void AssignToOwner_WhenOwnedByAnotherSubject_ThrowsDomainException()
    {
        // Arrange
        var cart = new Cart();
        cart.AssignToOwner(Guid.NewGuid());

        // Act & Assert
        var act = () => cart.AssignToOwner(Guid.NewGuid());
        act.Should().Throw<DomainException>()
           .Which.ErrorCode.Should().Be("cart_forbidden");
    }

    #endregion

    #region Calculations

    [TestMethod]
    public void LineTotal_WhenItemAdded_CalculatedCorrectly()
    {
        // Arrange
        var cart = new Cart();
        cart.AddItem(_availableProductId, "Bečka kobasica", 12.50m, 3);

        // Assert
        cart.Items.First().LineTotal.Should().Be(37.50m);
    }

    [TestMethod]
    public void TotalAmount_WhenMultipleItems_CalculatedCorrectly()
    {
        // Arrange
        var cart = new Cart();
        cart.AddItem(_availableProductId, "Bečka kobasica", 12.50m, 2);
        var productId2 = Guid.Parse("ca23a19d-7a8b-4e5f-9c1d-000000000002");
        cart.AddItem(productId2, "Kruh cjeloviti", 3.90m, 3);

        // Assert
        cart.TotalAmount.Should().Be(36.70m); // (12.50*2) + (3.90*3) = 25.00 + 11.70
        cart.TotalItems.Should().Be(5);
    }

    #endregion
}
