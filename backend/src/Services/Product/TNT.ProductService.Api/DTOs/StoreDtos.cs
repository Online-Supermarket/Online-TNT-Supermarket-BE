using System.ComponentModel.DataAnnotations;

namespace TNT.ProductService.Api.DTOs;

public class StoreAddressDto
{
    [Required]
    [NotBlank]
    [MaxLength(250)]
    public string Line1 { get; set; } = string.Empty;

    [MaxLength(250)]
    public string? Line2 { get; set; }

    [Required]
    [NotBlank]
    [MaxLength(100)]
    public string City { get; set; } = string.Empty;

    [Required]
    [NotBlank]
    [MaxLength(30)]
    public string PostalCode { get; set; } = string.Empty;

    [Required]
    [NotBlank]
    [MaxLength(100)]
    public string Country { get; set; } = string.Empty;
}

public class StoreRequest
{
    [Required]
    [NotBlank]
    [MaxLength(50)]
    public string StoreCode { get; set; } = string.Empty;

    [Required]
    [NotBlank]
    [MaxLength(200)]
    public string StoreName { get; set; } = string.Empty;

    [Required]
    public StoreAddressDto Address { get; set; } = new();

    [Required]
    [NotBlank]
    [MaxLength(30)]
    public string ContactNumber { get; set; } = string.Empty;

    [Required]
    [NotBlank]
    [EmailAddress]
    [MaxLength(254)]
    public string Email { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}

public class StoreResponse
{
    public Guid Id { get; set; }
    public string StoreCode { get; set; } = string.Empty;
    public string StoreName { get; set; } = string.Empty;
    public StoreAddressDto Address { get; set; } = new();
    public string ContactNumber { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

public class StoreListResponse
{
    public IReadOnlyList<StoreResponse> Items { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
