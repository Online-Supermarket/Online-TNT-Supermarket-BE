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
        var tax = decimal.Round(subtotal * .10m, 2, MidpointRounding.AwayFromZero);
        var deliveryFee = subtotal == 0 ? 0 : 5m;
        return new CheckoutTotals(subtotal, tax, deliveryFee, subtotal + tax + deliveryFee);
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
