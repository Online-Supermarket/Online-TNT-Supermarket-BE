namespace Order.Api;

public class OrderValidationService : IOrderValidationService
{
    private readonly ICatalogClient _catalogClient;

    public OrderValidationService(ICatalogClient catalogClient)
    {
        _catalogClient = catalogClient ?? throw new ArgumentNullException(nameof(catalogClient));
    }

    public async Task<BasketItemValidationResult> ValidateBasketItemAsync(Guid productId, int quantity)
    {
        if (productId == Guid.Empty)
        {
            throw new ArgumentException("Product ID cannot be empty.", nameof(productId));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        }

        var product = await _catalogClient.Product(productId);
        if (product is null)
        {
            return new BasketItemValidationResult(
                BasketValidationStatus.ProductNotFound,
                "Product is not available.",
                null
            );
        }

        if (!product.Active)
        {
            return new BasketItemValidationResult(
                BasketValidationStatus.ProductInactive,
                "Product is not available.",
                product
            );
        }

        if (quantity > product.StockQuantity)
        {
            return new BasketItemValidationResult(
                BasketValidationStatus.InsufficientStock,
                $"Only {product.StockQuantity} items are available in stock.",
                product
            );
        }

        return new BasketItemValidationResult(
            BasketValidationStatus.Success,
            null,
            product
        );
    }

    public CheckoutTotals CalculateOrderTotals(decimal subtotal)
    {
        return CheckoutRules.Calculate(subtotal);
    }
}
