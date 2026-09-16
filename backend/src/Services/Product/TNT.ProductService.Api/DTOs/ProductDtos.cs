using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace TNT.ProductService.Api.DTOs;

public class ProductRequest
{
    public Guid? CategoryId { get; set; }

    [Required]
    [NotBlank]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Range(0.01, 99999999)]
    public decimal Price { get; set; }

    [Range(0, int.MaxValue)]
    public int StockQuantity { get; set; }

    [Required]
    [NotBlank]
    [MaxLength(50)]
    public string Unit { get; set; } = "item";

    [MaxLength(500)]
    public string? ImageUrl { get; set; }

    public IFormFile? Image { get; set; }

    public bool IsActive { get; set; } = true;
}

public class ProductResponse
{
    public Guid Id { get; set; }
    public Guid? CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string? Category { get; set; }
    public int StockQuantity { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public bool Available { get; set; }
}

public class ProductListResponse
{
    public IReadOnlyList<ProductResponse> Items { get; set; } = [];
    public int TotalItems { get; set; }
    public int TotalPages { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
