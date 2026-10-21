namespace Order.Api;

public enum BasketValidationStatus
{
    Success,
    ProductNotFound,
    ProductInactive,
    InsufficientStock
}

public readonly record struct BasketItemValidationResult(
    BasketValidationStatus Status,
    string? Message,
    ProductDto? Product
);

public interface IOrderValidationService
{
    Task<BasketItemValidationResult> ValidateBasketItemAsync(Guid productId, int quantity);
    CheckoutTotals CalculateOrderTotals(decimal subtotal);
}
