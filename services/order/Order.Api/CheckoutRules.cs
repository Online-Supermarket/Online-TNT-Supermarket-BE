using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public readonly record struct CheckoutLine(Guid ProductId, int Quantity, decimal UnitPrice);
public readonly record struct CheckoutTotals(decimal Subtotal, decimal Tax, decimal DeliveryFee, decimal Total);

public static class CheckoutRules
{
    public static CheckoutTotals Calculate(decimal subtotal)
    {
        if (subtotal < 0) throw new ArgumentOutOfRangeException(nameof(subtotal));
        // TNT does not charge customer tax. Keep the field for backwards-compatible DTOs.
        const decimal tax = 0m;
        var deliveryFee = subtotal == 0 ? 0 : 450m;
        return new CheckoutTotals(subtotal, tax, deliveryFee, subtotal + deliveryFee);
    }

    public static string IntentHash(Guid addressId, IEnumerable<CheckoutLine> lines, string currency, decimal tax, decimal deliveryFee)
    {
        var source = JsonSerializer.Serialize(new
        {
            addressId,
            lines = lines.OrderBy(x => x.ProductId).Select(x => new { x.ProductId, x.Quantity, x.UnitPrice }),
            currency,
            tax,
            deliveryFee
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
    }
}
