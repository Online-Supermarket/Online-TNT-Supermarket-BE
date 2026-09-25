
using System;
using Xunit;

public class CheckoutRulesTests
{
    [Fact]
    public void Calculate_uses_correct_checkout_rules()
    {
        var result = CheckoutRules.Calculate(19.99m);

        Assert.Equal(19.99m, result.Subtotal);
        Assert.Equal(0m, result.Tax);
        Assert.Equal(450m, result.DeliveryFee);
        Assert.Equal(469.99m, result.Total);
    }

    [Fact]
    public void Calculate_empty_basket_has_no_fees()
    {
        var result = CheckoutRules.Calculate(0m);

        Assert.Equal(0m, result.Subtotal);
        Assert.Equal(0m, result.Tax);
        Assert.Equal(0m, result.DeliveryFee);
        Assert.Equal(0m, result.Total);
    }

    [Fact]
    public void Calculate_rejects_negative_subtotal()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CheckoutRules.Calculate(-10m)
        );
    }
}