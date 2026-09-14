using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TNT.ProductService.Api.DTOs;
using TNT.ProductService.Api.Services;

namespace TNT.ProductService.Api.Controllers;

[ApiController]
[Route("api/products")]
[AllowAnonymous]
[Produces("application/json")]
public class ProductsController : ControllerBase
{
    private static readonly string[] SupportedSortFields = ["name", "price"];
    private readonly TNT.ProductService.Api.Services.ProductService _productService;

    public ProductsController(TNT.ProductService.Api.Services.ProductService productService)
    {
        _productService = productService;
    }

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
}