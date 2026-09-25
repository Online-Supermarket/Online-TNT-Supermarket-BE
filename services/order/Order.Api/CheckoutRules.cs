
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public readonly record struct CheckoutLine(
    Guid ProductId,
    int Quantity,
    decimal UnitPrice
);

public readonly record struct CheckoutTotals(
    decimal Subtotal,
    decimal Tax,
    decimal DeliveryFee,
    decimal Total
);

public static class CheckoutRules
{
    private const decimal StandardDeliveryFee = 450m;

    public static CheckoutTotals Calculate(decimal subtotal)
    {
        if (subtotal < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(subtotal),
                "Subtotal cannot be negative."
            );
        }

        // TNT Supermarket does not charge customer tax.
        // Keep Tax for backwards-compatible DTOs.
        const decimal tax = 0m;

        // Empty baskets have no delivery charge.
        decimal deliveryFee = subtotal == 0m
            ? 0m
            : StandardDeliveryFee;

        decimal total = subtotal + tax + deliveryFee;

        return new CheckoutTotals(
            Subtotal: subtotal,
            Tax: tax,
            DeliveryFee: deliveryFee,
            Total: total
        );
    }

    public static string IntentHash(
        Guid addressId,
        IEnumerable<CheckoutLine> lines,
        string currency,
        decimal tax,
        decimal deliveryFee)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(currency);

        var source = JsonSerializer.Serialize(new
        {
            addressId,

            lines = lines
                .OrderBy(x => x.ProductId)
                .ThenBy(x => x.UnitPrice)
                .ThenBy(x => x.Quantity)
                .Select(x => new
                {
                    x.ProductId,
                    x.Quantity,
                    x.UnitPrice
                }),

            currency,
            tax,
            deliveryFee
        });

        return Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(source)
            )
        );
    }
}