using Xunit;

namespace Marketflow.UnitTests;

public sealed class OrderCalculationTests
{
    // =========================================================================
    // 1. ORDER CALCULATIONS - VALID INPUTS (THEORY & INLINEDATA)
    // =========================================================================

    [Theory]
    [InlineData(10.00, 450.00, 460.00)]
    [InlineData(19.99, 450.00, 469.99)]
    [InlineData(100.00, 450.00, 550.00)]
    [InlineData(500.50, 450.00, 950.50)]
    [InlineData(1250.00, 450.00, 1700.00)]
    [InlineData(10000.00, 450.00, 10450.00)]
    [InlineData(50000.00, 450.00, 50450.00)]
    public void Calculate_ValidSubtotals_ComputesStandardDeliveryAndTotalCorrectly(
        decimal subtotal,
        decimal expectedDeliveryFee,
        decimal expectedTotal)
    {
        // Act
        var result = CheckoutRules.Calculate(subtotal);

        // Assert
        Assert.Equal(subtotal, result.Subtotal);
        Assert.Equal(0m, result.Tax); // TNT Supermarket does not charge customer tax
        Assert.Equal(expectedDeliveryFee, result.DeliveryFee);
        Assert.Equal(expectedTotal, result.Total);
    }

    // =========================================================================
    // 2. BOUNDARY VALUE TESTING (EMPTY BASKET & MINIMUM PURCHASE)
    // =========================================================================

    [Theory]
    [InlineData(0.00, 0.00, 0.00)]       // Boundary: Empty basket has NO delivery fee and total is 0
    [InlineData(0.01, 450.00, 450.01)]   // Boundary: Smallest positive non-zero subtotal incurs standard delivery fee
    [InlineData(449.99, 450.00, 899.99)] // Boundary: Just below standard delivery fee amount
    [InlineData(450.00, 450.00, 900.00)] // Boundary: Exactly equal to standard delivery fee amount
    public void Calculate_BoundarySubtotals_AppliesCorrectDeliveryFeeThreshold(
        decimal subtotal,
        decimal expectedDeliveryFee,
        decimal expectedTotal)
    {
        // Act
        var result = CheckoutRules.Calculate(subtotal);

        // Assert
        Assert.Equal(subtotal, result.Subtotal);
        Assert.Equal(0m, result.Tax);
        Assert.Equal(expectedDeliveryFee, result.DeliveryFee);
        Assert.Equal(expectedTotal, result.Total);
    }

    // =========================================================================
    // 3. INVALID INPUTS & EXCEPTION HANDLING (THEORY & INLINEDATA)
    // =========================================================================

    [Theory]
    [InlineData(-0.01)]   // Boundary value immediately below zero
    [InlineData(-0.001)]  // Fractional boundary below zero
    [InlineData(-1.00)]   // Negative subtotal
    [InlineData(-50.00)]  // Arbitrary negative subtotal
    [InlineData(-500.00)] // Large negative subtotal
    public void Calculate_NegativeSubtotal_ThrowsArgumentOutOfRangeException(decimal negativeSubtotal)
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => CheckoutRules.Calculate(negativeSubtotal)
        );

        Assert.Equal("subtotal", ex.ParamName);
        Assert.Contains("Subtotal cannot be negative.", ex.Message);
    }

    // =========================================================================
    // 4. CHECKOUT INTENT HASH & DETERMINISTIC ORDER CHECKSUM
    // =========================================================================

    [Fact]
    public void IntentHash_GeneratesConsistentSha256Checksum_ForSameOrderLinesRegardlessOfOrder()
    {
        // Arrange
        var addressId = Guid.NewGuid();
        var productId1 = Guid.NewGuid();
        var productId2 = Guid.NewGuid();

        var lines1 = new List<CheckoutLine>
        {
            new(productId1, 2, 150m),
            new(productId2, 1, 300m)
        };

        var lines2 = new List<CheckoutLine>
        {
            new(productId2, 1, 300m), // Reversed order to test deterministic sort
            new(productId1, 2, 150m)
        };

        // Act
        var hash1 = CheckoutRules.IntentHash(addressId, lines1, "LKR", 0m, 450m);
        var hash2 = CheckoutRules.IntentHash(addressId, lines2, "LKR", 0m, 450m);

        // Assert
        Assert.NotNull(hash1);
        Assert.Equal(64, hash1.Length); // SHA256 hex string is 64 characters
        Assert.Equal(hash1, hash2);      // Idempotency: order of lines in collection must produce identical hash
    }

    [Fact]
    public void IntentHash_DifferentInputs_ProduceDifferentChecksums()
    {
        // Arrange
        var address1 = Guid.NewGuid();
        var address2 = Guid.NewGuid();
        var product = Guid.NewGuid();
        var lines = new List<CheckoutLine> { new(product, 1, 200m) };

        // Act
        var baseHash = CheckoutRules.IntentHash(address1, lines, "LKR", 0m, 450m);
        var diffAddressHash = CheckoutRules.IntentHash(address2, lines, "LKR", 0m, 450m);
        var diffCurrencyHash = CheckoutRules.IntentHash(address1, lines, "USD", 0m, 450m);
        var diffFeeHash = CheckoutRules.IntentHash(address1, lines, "LKR", 0m, 500m);

        // Assert
        Assert.NotEqual(baseHash, diffAddressHash);
        Assert.NotEqual(baseHash, diffCurrencyHash);
        Assert.NotEqual(baseHash, diffFeeHash);
    }

    [Fact]
    public void IntentHash_ThrowsArgumentNullException_WhenLinesOrCurrencyIsNull()
    {
        // Assert
        Assert.Throws<ArgumentNullException>(
            () => CheckoutRules.IntentHash(Guid.NewGuid(), null!, "LKR", 0m, 450m)
        );
        Assert.Throws<ArgumentNullException>(
            () => CheckoutRules.IntentHash(Guid.NewGuid(), new List<CheckoutLine>(), null!, 0m, 450m)
        );
    }
}
