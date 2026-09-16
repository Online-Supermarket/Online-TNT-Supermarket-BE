using System.ComponentModel.DataAnnotations;

namespace TNT.ProductService.Api.DTOs;

public class InventoryResponse
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int StockQuantity { get; set; }
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

public class InventoryUpdateRequest
{
    [Required]
    [Range(0, int.MaxValue, ErrorMessage = "Stock quantity cannot be negative.")]
    public int StockQuantity { get; set; }
}
