namespace Order.Api;

public static class PaymentRules
{
    public const long MaxReceiptSizeBytes = 5 * 1024 * 1024; // 5 MB
    public const string CashOnDelivery = "CashOnDelivery";
    public const string BankTransfer = "BankTransfer";

    public static readonly string[] AllowedMethods = [CashOnDelivery, BankTransfer];

    public static bool IsAllowedMethod(string? method) =>
        method is not null && (method == CashOnDelivery || method == BankTransfer);

    public static bool CanConfirmOrder(string paymentMethod, string paymentStatus)
    {
        if (paymentMethod == BankTransfer)
        {
            return paymentStatus == "Verified";
        }
        return true;
    }

    public static bool IsValidPdfMagicBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4) return false;
        // Check for '%PDF' header (% = 0x25, P = 0x50, D = 0x44, F = 0x46)
        return bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46;
    }

    public static (bool IsValid, string? Error) ValidateReceipt(string? contentType, long length, byte[]? headerBytes)
    {
        if (length <= 0) return (false, "A payment receipt PDF is required for Bank Transfer.");
        if (length > MaxReceiptSizeBytes) return (false, "Receipt PDF must not exceed 5 MB.");
        if (contentType != "application/pdf") return (false, "Receipt must be a PDF file (application/pdf).");
        if (headerBytes is null || !IsValidPdfMagicBytes(headerBytes)) return (false, "Uploaded file is not a valid PDF.");
        return (true, null);
    }
}
