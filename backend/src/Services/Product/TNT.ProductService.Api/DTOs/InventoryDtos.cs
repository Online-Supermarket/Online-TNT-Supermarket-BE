using System.ComponentModel.DataAnnotations;

namespace TNT.ProductService.Api.DTOs;

public class InventoryResponse
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int StockQuantity { get; set; }
    public int LowStockThreshold { get; set; } = 10;
    public bool IsLowStock => StockQuantity < LowStockThreshold;
    public string Unit { get; set; } = string.Empty;
    public DateTime? UpdatedAtUtc { get; set; }
    public bool Available => StockQuantity > 0;
}

public class InventoryListResponse
{
    public IEnumerable<InventoryResponse> Items { get; set; } = Array.Empty<InventoryResponse>();
    public int TotalItems { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => TotalItems > 0 ? (int)Math.Ceiling(TotalItems / (double)PageSize) : 0;
}

public class InventoryAddRequest
{
    [Required(ErrorMessage = "Product ID is required.")]
    public Guid ProductId { get; set; }

    [Required]
    [Range(0, int.MaxValue, ErrorMessage = "Stock quantity cannot be negative.")]
    public int StockQuantity { get; set; }
}

public class InventoryUpdateRequest
{
    [Required]
    [Range(0, int.MaxValue, ErrorMessage = "Stock quantity cannot be negative.")]
    public int StockQuantity { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Low stock threshold cannot be negative.")]
    public int? LowStockThreshold { get; set; }
}

public class StockAdjustRequest
{
    /// <summary>
    /// Adjustment type: "increase", "decrease", or "set".
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// Quantity associated with type ("increase", "decrease", "set").
    /// </summary>
    [Range(0, int.MaxValue, ErrorMessage = "Quantity cannot be negative.")]
    public int? Quantity { get; set; }

    /// <summary>
    /// Direct delta: positive to increase, negative to decrease.
    /// </summary>
    public int? Change { get; set; }

    /// <summary>
    /// Optional update to low stock threshold.
    /// </summary>
    [Range(0, int.MaxValue, ErrorMessage = "Low stock threshold cannot be negative.")]
    public int? LowStockThreshold { get; set; }
}

