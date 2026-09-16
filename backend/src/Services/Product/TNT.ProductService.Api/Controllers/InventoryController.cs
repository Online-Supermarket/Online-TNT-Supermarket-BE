using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TNT.ProductService.Api.DTOs;
using TNT.ProductService.Api.Services;

namespace TNT.ProductService.Api.Controllers;

/// <summary>
/// Manages inventory (stock levels) for products.
/// All endpoints require Admin or Staff role.
/// </summary>
[ApiController]
[Route("api/inventory")]
[Produces("application/json")]
[Authorize(Roles = "Admin,Staff")]
public class InventoryController : ControllerBase
{
    private readonly InventoryService _inventoryService;

    public InventoryController(InventoryService inventoryService)
    {
        _inventoryService = inventoryService;
    }

    // ── GET /api/inventory ────────────────────────────────────────────────────────
    /// <summary>Returns a paginated list of all active products with their current stock levels.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(InventoryListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<InventoryListResponse>> GetInventory(
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] bool? lowStock = null,
        CancellationToken ct = default)
    {
        if (page < 1)
            return BadRequest(new { message = "Page must be at least 1." });
        if (pageSize < 1 || pageSize > 100)
            return BadRequest(new { message = "Page size must be between 1 and 100." });

        var result = await _inventoryService.GetInventoryAsync(search, page, pageSize, lowStock, ct);
        return Ok(result);
    }

    // ── GET /api/inventory/{productId} ────────────────────────────────────────────
    /// <summary>Returns the inventory record for a specific product.</summary>
    [HttpGet("{productId:guid}")]
    [ProducesResponseType(typeof(InventoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InventoryResponse>> GetInventoryByProduct(Guid productId, CancellationToken ct)
    {
        var result = await _inventoryService.GetInventoryByProductAsync(productId, ct);

        if (result == null)
            return NotFound(new { message = "Product not found." });

        return Ok(result);
    }

    // ── POST /api/inventory ───────────────────────────────────────────────────────
    /// <summary>
    /// Initialises inventory for a product (sets its stock for the first time).
    /// Returns 409 Conflict if the product already has a stock record (stock > 0).
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(InventoryResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<InventoryResponse>> AddInventory(
        [FromBody] InventoryAddRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await _inventoryService.AddInventoryAsync(request, ct);
            return CreatedAtAction(
                nameof(GetInventoryByProduct),
                new { productId = result.ProductId },
                result);
        }
        catch (InventoryProductNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InventoryConflictException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    // ── PUT /api/inventory/{productId} ────────────────────────────────────────────
    /// <summary>Updates the stock quantity for an existing inventory record directly.</summary>
    [HttpPut("{productId:guid}")]
    [ProducesResponseType(typeof(InventoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InventoryResponse>> UpdateInventory(
        Guid productId,
        [FromBody] InventoryUpdateRequest request,
        CancellationToken ct)
    {
        if (request.StockQuantity < 0)
            return BadRequest(new { message = "Stock quantity cannot be negative." });

        var result = await _inventoryService.UpdateInventoryAsync(productId, request, ct);

        if (result == null)
            return NotFound(new { message = "Product not found." });

        return Ok(result);
    }

    // ── PATCH /api/inventory/{productId}/stock ────────────────────────────────────
    /// <summary>
    /// Adjusts stock atomically: supports increase, decrease, set, or change delta.
    /// Prevents negative stock.
    /// </summary>
    [HttpPatch("{productId:guid}/stock")]
    [ProducesResponseType(typeof(InventoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InventoryResponse>> AdjustStock(
        Guid productId,
        [FromBody] StockAdjustRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await _inventoryService.AdjustStockAsync(productId, request, ct);
            return Ok(result);
        }
        catch (InventoryProductNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InsufficientStockException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidStockAdjustmentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // ── DELETE /api/inventory/{productId} ─────────────────────────────────────────
    /// <summary>
    /// Removes the inventory record for a product by resetting its stock to 0.
    /// Returns 204 No Content on success.
    /// </summary>
    [HttpDelete("{productId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteInventory(Guid productId, CancellationToken ct)
    {
        var deleted = await _inventoryService.DeleteInventoryAsync(productId, ct);

        if (!deleted)
            return NotFound(new { message = "Product not found." });

        return NoContent();
    }
}
