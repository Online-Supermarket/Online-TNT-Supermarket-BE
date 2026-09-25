using Order.Api;
using Xunit;

public sealed class PaymentRulesTests
{
    [Theory]
    [InlineData("CashOnDelivery", true)]
    [InlineData("BankTransfer", true)]
    [InlineData("Card", false)]
    [InlineData("PayPal", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsAllowedMethod_validates_correctly(string? method, bool expected)
    {
        Assert.Equal(expected, PaymentRules.IsAllowedMethod(method));
    }

    [Fact]
    public void CanConfirmOrder_permits_cash_on_delivery_in_any_payment_status()
    {
        Assert.True(PaymentRules.CanConfirmOrder("CashOnDelivery", "NotRequired"));
        Assert.True(PaymentRules.CanConfirmOrder("CashOnDelivery", "PendingVerification"));
    }

    [Fact]
    public void CanConfirmOrder_requires_verified_status_for_bank_transfer()
    {
        Assert.True(PaymentRules.CanConfirmOrder("BankTransfer", "Verified"));
        Assert.False(PaymentRules.CanConfirmOrder("BankTransfer", "PendingVerification"));
        Assert.False(PaymentRules.CanConfirmOrder("BankTransfer", "Rejected"));
        Assert.False(PaymentRules.CanConfirmOrder("BankTransfer", "NotRequired"));
    }

    [Fact]
    public void IsValidPdfMagicBytes_identifies_pdf_header()
    {
        byte[] validPdfHeader = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34]; // %PDF-1.4
        byte[] invalidHeader = [0xFF, 0xD8, 0xFF, 0xE0]; // JPEG

        Assert.True(PaymentRules.IsValidPdfMagicBytes(validPdfHeader));
        Assert.False(PaymentRules.IsValidPdfMagicBytes(invalidHeader));
        Assert.False(PaymentRules.IsValidPdfMagicBytes(new byte[] { 0x25, 0x50 })); // Too short
    }

    [Fact]
    public void ValidateReceipt_rejects_empty_file()
    {
        var (isValid, error) = PaymentRules.ValidateReceipt("application/pdf", 0, [0x25, 0x50, 0x44, 0x46]);
        Assert.False(isValid);
        Assert.Contains("required", error);
    }

    [Fact]
    public void ValidateReceipt_rejects_file_over_5mb()
    {
        var (isValid, error) = PaymentRules.ValidateReceipt("application/pdf", 6 * 1024 * 1024, [0x25, 0x50, 0x44, 0x46]);
        Assert.False(isValid);
        Assert.Contains("5 MB", error);
    }

    [Fact]
    public void ValidateReceipt_rejects_non_pdf_content_type()
    {
        var (isValid, error) = PaymentRules.ValidateReceipt("image/jpeg", 1024, [0x25, 0x50, 0x44, 0x46]);
        Assert.False(isValid);
        Assert.Contains("application/pdf", error);
    }

    [Fact]
    public void ValidateReceipt_rejects_invalid_magic_bytes()
    {
        var (isValid, error) = PaymentRules.ValidateReceipt("application/pdf", 1024, [0x00, 0x00, 0x00, 0x00]);
        Assert.False(isValid);
        Assert.Contains("not a valid PDF", error);
    }

    [Fact]
    public void ValidateReceipt_accepts_valid_pdf()
    {
        var (isValid, error) = PaymentRules.ValidateReceipt("application/pdf", 1024 * 50, [0x25, 0x50, 0x44, 0x46, 0x2D]);
        Assert.True(isValid);
        Assert.Null(error);
    }
}
