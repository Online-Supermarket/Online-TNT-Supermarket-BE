using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TNT.ProductService.Api.DTOs;
using TNT.ProductService.Api.Services;

namespace TNT.ProductService.Api.Controllers;

[ApiController]
[Route("api/products")]
[Produces("application/json")]
public class ProductsController : ControllerBase
{
    private static readonly string[] SupportedSortFields = ["name", "price"];
    private readonly TNT.ProductService.Api.Services.ProductService _productService;

    public ProductsController(TNT.ProductService.Api.Services.ProductService productService)
    {
        _productService = productService;
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductResponse>> GetProduct(Guid id, CancellationToken ct)
    {
        var product = await _productService.GetProductAsync(id, ct);
        if (product == null) return NotFound(new { message = "Product not found." });

        return Ok(product);
    }

    [AllowAnonymous]
    [HttpGet]
    [ProducesResponseType(typeof(ProductListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ProductListResponse>> GetProducts(
        [FromQuery] string? search = null,
        [FromQuery] string? category = null,
        [FromQuery] decimal? minPrice = null,
        [FromQuery] decimal? maxPrice = null,
        [FromQuery] bool? available = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortOrder = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        if (page < 1) return BadRequest(new { message = "Page must be at least 1." });
        if (pageSize < 1 || pageSize > 100) return BadRequest(new { message = "Page size must be between 1 and 100." });
        if (minPrice < 0 || maxPrice < 0) return BadRequest(new { message = "Prices cannot be negative." });
        if (minPrice.HasValue && maxPrice.HasValue && minPrice > maxPrice)
            return BadRequest(new { message = "Minimum price cannot be greater than maximum price." });
        if (!string.IsNullOrWhiteSpace(sortBy) && !SupportedSortFields.Contains(sortBy, StringComparer.OrdinalIgnoreCase))
            return BadRequest(new { message = "Unsupported sort field. Use name or price." });
        if (!string.IsNullOrWhiteSpace(sortOrder) &&
            !string.Equals(sortOrder, "asc", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(sortOrder, "desc", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Unsupported sort order. Use asc or desc." });

        var result = await _productService.GetProductsAsync(
            search, category, minPrice, maxPrice, available, sortBy, sortOrder, page, pageSize, ct);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Staff")]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ProductResponse>> CreateProduct([FromForm] ProductRequest request, CancellationToken ct)
    {
        try
        {
            var created = await _productService.CreateProductAsync(request, ct);
            return CreatedAtAction(nameof(GetProduct), new { id = created.Id }, created);
        }
        catch (InvalidProductCategoryException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidProductImageException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,Staff")]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductResponse>> UpdateProduct(Guid id, [FromForm] ProductRequest request, CancellationToken ct)
    {
        ProductResponse? updated;
        try
        {
            updated = await _productService.UpdateProductAsync(id, request, ct);
        }
        catch (InvalidProductCategoryException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidProductImageException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        if (updated == null) return NotFound(new { message = "Product not found." });

        return Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,Staff")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteProduct(Guid id, CancellationToken ct)
    {
        var deleted = await _productService.DeleteProductAsync(id, ct);
        if (!deleted) return NotFound(new { message = "Product not found." });

        return NoContent();
    }

    [HttpPost("upload")]
    [Authorize(Roles = "Admin,Staff")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadImage(IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { message = "No file uploaded." });
        }

        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

        if (!allowedExtensions.Contains(extension))
        {
            return BadRequest(new { message = "Invalid file type. Only JPG, PNG, GIF, and WEBP are allowed." });
        }

        var fileName = $"{Guid.NewGuid()}{extension}";
        var folderPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images");

        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        var filePath = Path.Combine(folderPath, fileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        // Return relative URL that will be served by app.UseStaticFiles()
        return Ok(new { url = $"/images/{fileName}" });
    }
}
