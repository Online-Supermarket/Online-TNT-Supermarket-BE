using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TNT.ProductService.Api.Data;
using TNT.ProductService.Api.DTOs;

namespace TNT.ProductService.Api.Controllers;

[ApiController]
[Route("api/inventory")]
[Produces("application/json")]
public class InventoryController : ControllerBase
{
    private readonly ProductDbContext _dbContext;

    public InventoryController(ProductDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    [Authorize(Roles = "Admin,Staff")]
    [ProducesResponseType(typeof(InventoryListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<InventoryListResponse>> GetInventory(
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        if (page < 1) return BadRequest(new { message = "Page must be at least 1." });
        if (pageSize < 1 || pageSize > 100) return BadRequest(new { message = "Page size must be between 1 and 100." });

        var query = _dbContext.Products.AsNoTracking().Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchTerm = search.ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(searchTerm));
        }

        var totalItems = await query.CountAsync(ct);

        var products = await query
            .OrderBy(p => p.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new InventoryResponse
            {
                ProductId = p.Id,
                ProductName = p.Name,
                StockQuantity = p.StockQuantity,
                Unit = p.Unit,
                UpdatedAtUtc = p.UpdatedAtUtc
            })
            .ToListAsync(ct);

        return Ok(new InventoryListResponse
        {
            TotalItems = totalItems,
            Page = page,
            PageSize = pageSize,
            Items = products
        });
    }

    [HttpGet("{productId:guid}")]
    [Authorize(Roles = "Admin,Staff")]
    [ProducesResponseType(typeof(InventoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InventoryResponse>> GetInventoryByProduct(Guid productId, CancellationToken ct)
    {
        var product = await _dbContext.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productId && p.IsActive, ct);

        if (product == null)
            return NotFound(new { message = "Product not found." });

        return Ok(new InventoryResponse
        {
            ProductId = product.Id,
            ProductName = product.Name,
            StockQuantity = product.StockQuantity,
            Unit = product.Unit,
            UpdatedAtUtc = product.UpdatedAtUtc
        });
    }

    [HttpPut("{productId:guid}")]
    [Authorize(Roles = "Admin,Staff")]
    [ProducesResponseType(typeof(InventoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InventoryResponse>> UpdateInventory(Guid productId, [FromBody] InventoryUpdateRequest request, CancellationToken ct)
    {
        if (request.StockQuantity < 0)
        {
            return BadRequest(new { message = "Stock quantity cannot be negative." });
        }

        var product = await _dbContext.Products
            .FirstOrDefaultAsync(p => p.Id == productId && p.IsActive, ct);

        if (product == null)
            return NotFound(new { message = "Product not found." });

        product.StockQuantity = request.StockQuantity;
        product.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(ct);

        return Ok(new InventoryResponse
        {
            ProductId = product.Id,
            ProductName = product.Name,
            StockQuantity = product.StockQuantity,
            Unit = product.Unit,
            UpdatedAtUtc = product.UpdatedAtUtc
        });
    }
}
