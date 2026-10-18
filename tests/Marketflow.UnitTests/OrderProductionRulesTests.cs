using Order.Api;
using Xunit;

namespace Marketflow.UnitTests;

public sealed class OrderProductionRulesTests
{
    // =========================================================================
    // 1. ADDRESS VALIDATION RULES (ACTUAL PRODUCTION LOGIC IN ORDER.API)
    // =========================================================================

    [Fact]
    public void AddressInput_Validate_AcceptsCompleteAddress()
    {
        // Arrange
        var address = new AddressInput(
            RecipientName: "Kamal Perera",
            Phone: "0771234567",
            Line1: "123 Galle Road",
            Line2: "Apartment 4B",
            City: "Colombo",
            Zone: "Western"
        );

        // Act
        var errors = AddressInput.Validate(address);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void AddressInput_Validate_AcceptsAddressWithoutOptionalLine2()
    {
        // Arrange
        var address = new AddressInput(
            RecipientName: "Nimal Silva",
            Phone: "0719876543",
            Line1: "45 Kandy Road",
            Line2: null,
            City: "Kadawatha",
            Zone: "Western"
        );

        // Act
        var errors = AddressInput.Validate(address);

        // Assert
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("", "0771234567", "123 Main St", "Colombo", "Western")]          // Empty recipient
    [InlineData("   ", "0771234567", "123 Main St", "Colombo", "Western")]       // Whitespace recipient
    [InlineData("Kamal", "", "123 Main St", "Colombo", "Western")]               // Empty phone
    [InlineData("Kamal", "   ", "123 Main St", "Colombo", "Western")]            // Whitespace phone
    [InlineData("Kamal", "0771234567", "", "Colombo", "Western")]                // Empty Line1
    [InlineData("Kamal", "0771234567", "   ", "Colombo", "Western")]             // Whitespace Line1
    [InlineData("Kamal", "0771234567", "123 Main St", "", "Western")]            // Empty City
    [InlineData("Kamal", "0771234567", "123 Main St", "   ", "Western")]         // Whitespace City
    [InlineData("Kamal", "0771234567", "123 Main St", "Colombo", "")]            // Empty Zone
    [InlineData("Kamal", "0771234567", "123 Main St", "Colombo", "   ")]         // Whitespace Zone
    public void AddressInput_Validate_RejectsMissingRequiredFields(
        string recipient,
        string phone,
        string line1,
        string city,
        string zone)
    {
        // Arrange
        var address = new AddressInput(recipient, phone, line1, null, city, zone);

        // Act
        var errors = AddressInput.Validate(address);

        // Assert
        Assert.True(errors.ContainsKey("address"));
        Assert.Contains("Recipient, phone, line 1, city and zone are required.", errors["address"][0]);
    }

    // =========================================================================
    // 2. BANK CONFIGURATION RULES (BANK TRANSFER AVAILABILITY CHECK)
    // =========================================================================

    [Fact]
    public void BankConfig_IsConfigured_ReturnsTrueWhenAllFieldsArePresent()
    {
        var config = new BankConfig("Bank of Ceylon", "TNT Supermarket", "0012345678", "Colombo Main");
        Assert.True(config.IsConfigured);
    }

    [Theory]
    [InlineData(null, "TNT Supermarket", "0012345678", "Colombo Main")]
    [InlineData("", "TNT Supermarket", "0012345678", "Colombo Main")]
    [InlineData("   ", "TNT Supermarket", "0012345678", "Colombo Main")]
    [InlineData("Bank of Ceylon", null, "0012345678", "Colombo Main")]
    [InlineData("Bank of Ceylon", "", "0012345678", "Colombo Main")]
    [InlineData("Bank of Ceylon", "TNT Supermarket", null, "Colombo Main")]
    [InlineData("Bank of Ceylon", "TNT Supermarket", "", "Colombo Main")]
    [InlineData("Bank of Ceylon", "TNT Supermarket", "0012345678", null)]
    [InlineData("Bank of Ceylon", "TNT Supermarket", "0012345678", "")]
    public void BankConfig_IsConfigured_ReturnsFalseWhenAnyRequiredFieldIsMissing(
        string? bankName,
        string? accountName,
        string? accountNumber,
        string? branch)
    {
        var config = new BankConfig(bankName, accountName, accountNumber, branch);
        Assert.False(config.IsConfigured);
    }

    // =========================================================================
    // 3. PAYMENT RECEIPT EXACT BOUNDARIES & PDF HEADER CHECKS
    // =========================================================================

    [Fact]
    public void ValidateReceipt_AcceptsExact5MbBoundary()
    {
        // 5 MB exactly = 5 * 1024 * 1024 = 5,242,880 bytes
        const long exact5Mb = 5 * 1024 * 1024;
        byte[] validPdfHeader = [0x25, 0x50, 0x44, 0x46]; // %PDF

        var (isValid, error) = PaymentRules.ValidateReceipt("application/pdf", exact5Mb, validPdfHeader);

        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidateReceipt_Rejects5MbPlusOneByteBoundary()
    {
        // 5 MB + 1 byte = 5,242,881 bytes
        const long over5Mb = (5 * 1024 * 1024) + 1;
        byte[] validPdfHeader = [0x25, 0x50, 0x44, 0x46];

        var (isValid, error) = PaymentRules.ValidateReceipt("application/pdf", over5Mb, validPdfHeader);

        Assert.False(isValid);
        Assert.Contains("Receipt PDF must not exceed 5 MB.", error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-1024)]
    public void ValidateReceipt_RejectsZeroOrNegativeLengths(long invalidLength)
    {
        byte[] validPdfHeader = [0x25, 0x50, 0x44, 0x46];

        var (isValid, error) = PaymentRules.ValidateReceipt("application/pdf", invalidLength, validPdfHeader);

        Assert.False(isValid);
        Assert.Contains("A payment receipt PDF is required for Bank Transfer.", error);
    }

    [Fact]
    public void ValidateReceipt_RejectsHeaderWithFewerThanFourBytes()
    {
        byte[] tooShortHeader = [0x25, 0x50, 0x44]; // Only 3 bytes

        var (isValid, error) = PaymentRules.ValidateReceipt("application/pdf", 1024, tooShortHeader);

        Assert.False(isValid);
        Assert.Contains("Uploaded file is not a valid PDF.", error);
    }
}
