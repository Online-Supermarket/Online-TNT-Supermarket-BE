using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TNT.ProductService.Api.DTOs;
using TNT.ProductService.Api.Services;

namespace TNT.ProductService.Api.Controllers;

[ApiController]
[Route("api/categories")]
[Produces("application/json")]
public class CategoriesController : ControllerBase
{
    private readonly CategoryService _categoryService;

    public CategoriesController(CategoryService categoryService)
    {
        _categoryService = categoryService;
    }

    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CategoryListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<CategoryListResponse>> GetCategories(
        [FromQuery] bool includeInactive = true,
        CancellationToken ct = default)
    {
        var result = await _categoryService.GetCategoriesAsync(includeInactive, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CategoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CategoryResponse>> GetCategory(Guid id, CancellationToken ct)
    {
        var category = await _categoryService.GetCategoryAsync(id, ct);
        if (category == null) return NotFound(new { message = "Category not found." });

        return Ok(category);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Staff")]
    [ProducesResponseType(typeof(CategoryResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CategoryResponse>> CreateCategory([FromBody] CategoryRequest request, CancellationToken ct)
    {
        try
        {
            var created = await _categoryService.CreateCategoryAsync(request, ct);
            return CreatedAtAction(nameof(GetCategory), new { id = created.Id }, created);
        }
        catch (CategoryConflictException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,Staff")]
    [ProducesResponseType(typeof(CategoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CategoryResponse>> UpdateCategory(Guid id, [FromBody] CategoryRequest request, CancellationToken ct)
    {
        CategoryResponse? updated;
        try
        {
            updated = await _categoryService.UpdateCategoryAsync(id, request, ct);
        }
        catch (CategoryConflictException ex)
        {
            return Conflict(new { message = ex.Message });
        }

        if (updated == null) return NotFound(new { message = "Category not found." });

        return Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,Staff")]
    [ProducesResponseType(typeof(CategoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CategoryResponse>> DeleteCategory(Guid id, CancellationToken ct)
    {
        var category = await _categoryService.DeactivateCategoryAsync(id, ct);
        if (category == null) return NotFound(new { message = "Category not found." });

        return Ok(category);
    }

    [HttpPatch("{id:guid}/activate")]
    [Authorize(Roles = "Admin,Staff")]
    [ProducesResponseType(typeof(CategoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CategoryResponse>> ActivateCategory(Guid id, CancellationToken ct)
    {
        var category = await _categoryService.ActivateCategoryAsync(id, ct);
        if (category == null) return NotFound(new { message = "Category not found." });

        return Ok(category);
    }

    [HttpPatch("{id:guid}/deactivate")]
    [Authorize(Roles = "Admin,Staff")]
    [ProducesResponseType(typeof(CategoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CategoryResponse>> DeactivateCategory(Guid id, CancellationToken ct)
    {
        var category = await _categoryService.DeactivateCategoryAsync(id, ct);
        if (category == null) return NotFound(new { message = "Category not found." });

        return Ok(category);
    }
}
