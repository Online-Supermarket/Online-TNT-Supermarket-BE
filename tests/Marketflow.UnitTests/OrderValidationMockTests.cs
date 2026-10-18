using Moq;
using Order.Api;
using Xunit;

namespace Marketflow.UnitTests;

public sealed class OrderValidationMockTests
{
    private readonly Mock<ICatalogClient> _catalogMock;
    private readonly OrderValidationService _orderValidationService;

    public OrderValidationMockTests()
    {
        _catalogMock = new Mock<ICatalogClient>(MockBehavior.Strict);
        _orderValidationService = new OrderValidationService(_catalogMock.Object);
    }

    // =========================================================================
    // 1. CONSTRUCTOR VALIDATION
    // =========================================================================

    [Fact]
    public void Constructor_WhenCatalogClientIsNull_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new OrderValidationService(null!));
        Assert.Equal("catalogClient", ex.ParamName);
    }

    // =========================================================================
    // 2. PRODUCT VALIDATION & INVALID PRODUCT HANDLING
    // =========================================================================

    [Fact]
    public async Task ValidateBasketItem_WhenProductDoesNotExist_ReturnsProductNotFound()
    {
        // Arrange
        var nonExistentProductId = Guid.NewGuid();
        _catalogMock
            .Setup(c => c.Product(nonExistentProductId))
            .ReturnsAsync((ProductDto?)null);

        // Act
        var result = await _orderValidationService.ValidateBasketItemAsync(nonExistentProductId, 2);

        // Assert: Verify exact result structure and returned state
        Assert.Equal(BasketValidationStatus.ProductNotFound, result.Status);
        Assert.Equal("Product is not available.", result.Message);
        Assert.Null(result.Product);

        // Verify: Mock was invoked once with the expected ID
        _catalogMock.Verify(c => c.Product(nonExistentProductId), Times.Once);
    }

    [Fact]
    public async Task ValidateBasketItem_WhenProductIsInactive_ReturnsProductInactive()
    {
        // Arrange
        var inactiveProductId = Guid.NewGuid();
        var inactiveProduct = new ProductDto(
            Id: inactiveProductId,
            Sku: "TNT-INACTIVE-001",
            Name: "Seasonal Apples",
            Price: 350.00m,
            StockQuantity: 20,
            Active: false,
            ImageUrl: null
        );

        _catalogMock
            .Setup(c => c.Product(inactiveProductId))
            .ReturnsAsync(inactiveProduct);

        // Act
        var result = await _orderValidationService.ValidateBasketItemAsync(inactiveProductId, 2);

        // Assert: Product is returned but marked inactive
        Assert.Equal(BasketValidationStatus.ProductInactive, result.Status);
        Assert.Equal("Product is not available.", result.Message);
        Assert.NotNull(result.Product);
        Assert.False(result.Product.Active);
        Assert.Equal("TNT-INACTIVE-001", result.Product.Sku);

        // Verify: Mock was invoked once
        _catalogMock.Verify(c => c.Product(inactiveProductId), Times.Once);
    }

    // =========================================================================
    // 3. INPUT VALIDATION & BOUNDARY TESTING (EARLY REJECTION & TIMES.NEVER)
    // =========================================================================

    [Theory]
    [InlineData(0)]    // Boundary value: zero quantity
    [InlineData(-1)]   // Boundary value: immediately below zero
    [InlineData(-100)] // Extreme negative quantity
    public async Task ValidateBasketItem_WhenQuantityIsZeroOrNegative_ThrowsArgumentOutOfRangeException(int invalidQuantity)
    {
        // Arrange
        var productId = Guid.NewGuid();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _orderValidationService.ValidateBasketItemAsync(productId, invalidQuantity)
        );
        Assert.Equal("quantity", ex.ParamName);
        Assert.Contains("Quantity must be positive.", ex.Message);

        // Verify: Mock dependency must NEVER be called when input parameter fails early
        _catalogMock.Verify(c => c.Product(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task ValidateBasketItem_WhenProductIdIsEmpty_ThrowsArgumentException()
    {
        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _orderValidationService.ValidateBasketItemAsync(Guid.Empty, 1)
        );
        Assert.Equal("productId", ex.ParamName);
        Assert.Contains("Product ID cannot be empty.", ex.Message);

        // Verify: Mock dependency is never invoked for empty Guid
        _catalogMock.Verify(c => c.Product(It.IsAny<Guid>()), Times.Never);
    }

    // =========================================================================
    // 4. INSUFFICIENT STOCK & STOCK BOUNDARIES (THEORY & INLINEDATA)
    // =========================================================================

    [Theory]
    [InlineData(1, 0)]     // Boundary value: zero remaining stock (Out of stock)
    [InlineData(6, 5)]     // Boundary value: exactly 1 unit exceeding stock
    [InlineData(10, 5)]    // Double available stock
    [InlineData(501, 500)] // Large stock boundary exceeding by 1
    public async Task ValidateBasketItem_WhenQuantityExceedsAvailableStock_ReturnsInsufficientStock(
        int requestedQuantity,
        int availableStock)
    {
        // Arrange
        var productId = Guid.NewGuid();
        var lowStockProduct = new ProductDto(
            Id: productId,
            Sku: "TNT-MILK-001",
            Name: "Fresh Milk 1L",
            Price: 420.00m,
            StockQuantity: availableStock,
            Active: true,
            ImageUrl: "/images/milk.png"
        );

        _catalogMock
            .Setup(c => c.Product(productId))
            .ReturnsAsync(lowStockProduct);

        // Act
        var result = await _orderValidationService.ValidateBasketItemAsync(productId, requestedQuantity);

        // Assert: Detailed outcome assertion
        Assert.Equal(BasketValidationStatus.InsufficientStock, result.Status);
        Assert.Equal($"Only {availableStock} items are available in stock.", result.Message);
        Assert.NotNull(result.Product);
        Assert.Equal(availableStock, result.Product.StockQuantity);

        // Verify: Mock called exactly once
        _catalogMock.Verify(c => c.Product(productId), Times.Once);
    }

    [Theory]
    [InlineData(1, 1)]     // Boundary value: single item stock exactly matched
    [InlineData(5, 5)]     // Boundary value: requested quantity equals remaining stock
    [InlineData(3, 10)]    // Standard case: within available stock
    [InlineData(100, 100)] // Boundary value: large batch exact match
    public async Task ValidateBasketItem_WhenQuantityAndStockAreValid_ReturnsSuccess(
        int requestedQuantity,
        int availableStock)
    {
        // Arrange
        var productId = Guid.NewGuid();
        var validProduct = new ProductDto(
            Id: productId,
            Sku: "TNT-RICE-001",
            Name: "Basmati Rice 5kg",
            Price: 1650.00m,
            StockQuantity: availableStock,
            Active: true,
            ImageUrl: null
        );

        _catalogMock
            .Setup(c => c.Product(productId))
            .ReturnsAsync(validProduct);

        // Act
        var result = await _orderValidationService.ValidateBasketItemAsync(productId, requestedQuantity);

        // Assert: Outcomes verified
        Assert.Equal(BasketValidationStatus.Success, result.Status);
        Assert.Null(result.Message);
        Assert.NotNull(result.Product);
        Assert.Equal(validProduct.Name, result.Product.Name);
        Assert.Equal(validProduct.Price, result.Product.Price);
        Assert.Equal(availableStock, result.Product.StockQuantity);

        // Verify: Mock invoked once
        _catalogMock.Verify(c => c.Product(productId), Times.Once);
    }

    // =========================================================================
    // 5. FAILURE PATHS: DOWNSTREAM SERVICE FAILURES & TIMEOUTS
    // =========================================================================

    [Fact]
    public async Task ValidateBasketItem_WhenCatalogServiceThrowsHttpRequestException_PropagatesException()
    {
        // Arrange
        var productId = Guid.NewGuid();
        _catalogMock
            .Setup(c => c.Product(productId))
            .ThrowsAsync(new HttpRequestException("Catalog microservice network failure."));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => _orderValidationService.ValidateBasketItemAsync(productId, 2)
        );
        Assert.Equal("Catalog microservice network failure.", ex.Message);

        // Verify: Product lookup was attempted once before failing
        _catalogMock.Verify(c => c.Product(productId), Times.Once);
    }

    [Fact]
    public async Task ValidateBasketItem_WhenCatalogServiceTimesOut_PropagatesTaskCanceledException()
    {
        // Arrange
        var productId = Guid.NewGuid();
        _catalogMock
            .Setup(c => c.Product(productId))
            .ThrowsAsync(new TaskCanceledException("The request timed out."));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<TaskCanceledException>(
            () => _orderValidationService.ValidateBasketItemAsync(productId, 2)
        );
        Assert.Equal("The request timed out.", ex.Message);

        // Verify: Mock was invoked once
        _catalogMock.Verify(c => c.Product(productId), Times.Once);
    }

    // =========================================================================
    // 6. ORDER TOTAL CALCULATIONS DELEGATION
    // =========================================================================

    [Theory]
    [InlineData(0.00, 0.00, 0.00)]      // Empty cart boundary
    [InlineData(0.01, 450.00, 450.01)]  // Smallest positive subtotal
    [InlineData(1250.00, 450.00, 1700.00)]
    public void CalculateOrderTotals_DelegatesToCheckoutRules_ReturnsExpectedTotals(
        decimal subtotal,
        decimal expectedDeliveryFee,
        decimal expectedTotal)
    {
        // Act
        var totals = _orderValidationService.CalculateOrderTotals(subtotal);

        // Assert
        Assert.Equal(subtotal, totals.Subtotal);
        Assert.Equal(0m, totals.Tax);
        Assert.Equal(expectedDeliveryFee, totals.DeliveryFee);
        Assert.Equal(expectedTotal, totals.Total);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(-100.00)]
    public void CalculateOrderTotals_WhenSubtotalIsNegative_ThrowsArgumentOutOfRangeException(decimal negativeSubtotal)
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => _orderValidationService.CalculateOrderTotals(negativeSubtotal)
        );
        Assert.Equal("subtotal", ex.ParamName);
    }
}
