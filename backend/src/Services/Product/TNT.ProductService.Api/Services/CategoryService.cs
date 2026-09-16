using Microsoft.EntityFrameworkCore;
using TNT.ProductService.Api.Data;
using TNT.ProductService.Api.DTOs;
using TNT.ProductService.Api.Entities;

namespace TNT.ProductService.Api.Services;

public class CategoryService
{
    private readonly ProductDbContext _db;
    private readonly ILogger<CategoryService> _logger;

    public CategoryService(ProductDbContext db, ILogger<CategoryService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<CategoryListResponse> GetCategoriesAsync(bool includeInactive = true, CancellationToken ct = default)
    {
        var query = _db.Categories.AsNoTracking();
        if (!includeInactive) query = query.Where(category => category.IsActive);

        var categories = await query
            .OrderBy(category => category.Name)
            .Select(category => MapToResponse(category))
            .ToListAsync(ct);

        return new CategoryListResponse
        {
            Items = categories,
            Total = categories.Count
        };
    }

    public async Task<CategoryResponse?> GetCategoryAsync(Guid id, CancellationToken ct = default)
    {
        var category = await _db.Categories.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id, ct);

        return category == null ? null : MapToResponse(category);
    }

    public async Task<CategoryResponse> CreateCategoryAsync(CategoryRequest request, CancellationToken ct = default)
    {
        if (await CategoryNameExistsAsync(request.Name, excludeCategoryId: null, ct))
        {
            throw new CategoryConflictException("A category with this name already exists.");
        }

        var category = new Category
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };

        ApplyRequest(category, request);
        _db.Categories.Add(category);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created category {CategoryId} named {CategoryName}", category.Id, category.Name);
        return MapToResponse(category);
    }

    public async Task<CategoryResponse?> UpdateCategoryAsync(Guid id, CategoryRequest request, CancellationToken ct = default)
    {
        var category = await _db.Categories.FirstOrDefaultAsync(item => item.Id == id, ct);
        if (category == null) return null;

        if (await CategoryNameExistsAsync(request.Name, excludeCategoryId: id, ct))
        {
            throw new CategoryConflictException("A category with this name already exists.");
        }

        ApplyRequest(category, request);
        category.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Updated category {CategoryId}", category.Id);
        return MapToResponse(category);
    }

    public async Task<CategoryResponse?> ActivateCategoryAsync(Guid id, CancellationToken ct = default)
    {
        return await SetCategoryStatusAsync(id, true, ct);
    }

    public async Task<CategoryResponse?> DeactivateCategoryAsync(Guid id, CancellationToken ct = default)
    {
        return await SetCategoryStatusAsync(id, false, ct);
    }

    private async Task<CategoryResponse?> SetCategoryStatusAsync(Guid id, bool isActive, CancellationToken ct)
    {
        var category = await _db.Categories.FirstOrDefaultAsync(item => item.Id == id, ct);
        if (category == null) return null;

        category.IsActive = isActive;
        category.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("{Action} category {CategoryId}", isActive ? "Activated" : "Deactivated", category.Id);
        return MapToResponse(category);
    }

    private async Task<bool> CategoryNameExistsAsync(string name, Guid? excludeCategoryId, CancellationToken ct)
    {
        var normalizedName = name.Trim().ToLower();
        return await _db.Categories.AnyAsync(category =>
            category.Name.ToLower() == normalizedName &&
            (!excludeCategoryId.HasValue || category.Id != excludeCategoryId.Value), ct);
    }

    private static void ApplyRequest(Category category, CategoryRequest request)
    {
        category.Name = request.Name.Trim();
        category.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        category.IsActive = request.IsActive;
    }

    private static CategoryResponse MapToResponse(Category category) => new()
    {
        Id = category.Id,
        Name = category.Name,
        Description = category.Description,
        IsActive = category.IsActive,
        CreatedAt = category.CreatedAt,
        UpdatedAt = category.UpdatedAt
    };
}

public class CategoryConflictException : Exception
{
    public CategoryConflictException(string message) : base(message) { }
}
