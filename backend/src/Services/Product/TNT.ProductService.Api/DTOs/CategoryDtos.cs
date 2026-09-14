using System.ComponentModel.DataAnnotations;

namespace TNT.ProductService.Api.DTOs;

public class CategoryRequest
{
    [Required]
    [NotBlank]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(500)]
    public string? ImageUrl { get; set; }

    public bool IsActive { get; set; } = true;
}

public class CategoryResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

public class CategoryListResponse
{
    public IReadOnlyList<CategoryResponse> Items { get; set; } = [];
    public int Total { get; set; }
}
