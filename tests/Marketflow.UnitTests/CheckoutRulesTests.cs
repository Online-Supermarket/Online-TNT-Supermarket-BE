using Xunit;

public sealed class CheckoutRulesTests
{
    [Fact]
    public void Calculate_does_not_charge_tax_and_adds_delivery_fee()
    {
        var totals = CheckoutRules.Calculate(19.99m);

        Assert.Equal(19.99m, totals.Subtotal);
        Assert.Equal(0m, totals.Tax);
        Assert.Equal(450m, totals.DeliveryFee);
        Assert.Equal(469.99m, totals.Total);
    }

    [Fact]
    public void Calculate_has_no_delivery_fee_for_an_empty_basket()
    {
        var totals = CheckoutRules.Calculate(0m);

        Assert.Equal(0m, totals.Tax);
        Assert.Equal(0m, totals.DeliveryFee);
        Assert.Equal(0m, totals.Total);
    }

    [Fact]
    public void IntentHash_is_order_independent_but_changes_when_quantity_changes()
    {
        var address = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var first = new[] { new CheckoutLine(Guid.Parse("22222222-2222-2222-2222-222222222222"), 1, 4.50m), new CheckoutLine(Guid.Parse("11111111-1111-1111-1111-111111111111"), 2, 3m) };
        var reordered = first.Reverse();
        var changed = new[] { first[0] with { Quantity = 2 }, first[1] };

        var expected = CheckoutRules.IntentHash(address, first, "USD", 1.05m, 450m);

        Assert.Equal(expected, CheckoutRules.IntentHash(address, reordered, "USD", 1.05m, 450m));
        Assert.NotEqual(expected, CheckoutRules.IntentHash(address, changed, "USD", 1.05m, 450m));
    }
}
