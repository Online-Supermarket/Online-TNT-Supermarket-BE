using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TNT.ProductService.Api.DTOs;
using TNT.ProductService.Api.Services;

namespace TNT.ProductService.Api.Controllers;

[ApiController]
[Route("api/stores")]
[Authorize(Roles = "Admin")]
[Produces("application/json")]
public class StoresController : ControllerBase
{
    private readonly StoreService _storeService;

    public StoresController(StoreService storeService)
    {
        _storeService = storeService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(StoreListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<StoreListResponse>> GetStores(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] bool? isActive = null,
        CancellationToken ct = default)
    {
        var result = await _storeService.GetStoresAsync(page, pageSize, search, isActive, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(StoreResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StoreResponse>> GetStore(Guid id, CancellationToken ct)
    {
        var store = await _storeService.GetStoreAsync(id, ct);
        if (store == null) return NotFound(new { message = "Store not found." });

        return Ok(store);
    }

    [HttpPost]
    [ProducesResponseType(typeof(StoreResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<StoreResponse>> CreateStore([FromBody] StoreRequest request, CancellationToken ct)
    {
        try
        {
            var created = await _storeService.CreateStoreAsync(request, ct);
            return CreatedAtAction(nameof(GetStore), new { id = created.Id }, created);
        }
        catch (StoreConflictException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(StoreResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StoreResponse>> UpdateStore(Guid id, [FromBody] StoreRequest request, CancellationToken ct)
    {
        StoreResponse? updated;
        try
        {
            updated = await _storeService.UpdateStoreAsync(id, request, ct);
        }
        catch (StoreConflictException ex)
        {
            return Conflict(new { message = ex.Message });
        }

        if (updated == null) return NotFound(new { message = "Store not found." });

        return Ok(updated);
    }

    [HttpPatch("{id:guid}/activate")]
    [ProducesResponseType(typeof(StoreResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StoreResponse>> ActivateStore(Guid id, CancellationToken ct)
    {
        var store = await _storeService.ActivateStoreAsync(id, ct);
        if (store == null) return NotFound(new { message = "Store not found." });

        return Ok(store);
    }

    [HttpPatch("{id:guid}/deactivate")]
    [ProducesResponseType(typeof(StoreResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StoreResponse>> DeactivateStore(Guid id, CancellationToken ct)
    {
        var store = await _storeService.DeactivateStoreAsync(id, ct);
        if (store == null) return NotFound(new { message = "Store not found." });

        return Ok(store);
    }
}
