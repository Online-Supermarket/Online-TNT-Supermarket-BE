using Microsoft.EntityFrameworkCore;
using TNT.ProductService.Api.Data;
using TNT.ProductService.Api.DTOs;
using TNT.ProductService.Api.Entities;

namespace TNT.ProductService.Api.Services;

public class ProductService
{
    private readonly ProductDbContext _db;
    private readonly ILogger<ProductService> _logger;

    public ProductService(ProductDbContext db, ILogger<ProductService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ProductResponse?> GetProductAsync(Guid id, CancellationToken ct = default)
    {
        var product = await _db.Products.AsNoTracking()
            .Include(item => item.Category)
            .FirstOrDefaultAsync(item => item.Id == id, ct);

        return product == null || !product.IsActive ? null : MapToResponse(product);
    }

    public async Task<ProductListResponse> GetProductsAsync(
        string? search = null,
        string? category = null,
        decimal? minPrice = null,
        decimal? maxPrice = null,
        bool? available = null,
        string? sortBy = null,
        string? sortOrder = null,
        int page = 1,
        int pageSize = 10,
        CancellationToken ct = default)
    {
        var query = _db.Products
            .AsNoTracking()
            .Include(product => product.Category)
            .Where(product => product.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(product =>
                product.Name.ToLower().Contains(term) ||
                (product.Description != null && product.Description.ToLower().Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var categoryTerm = category.Trim().ToLower();
            query = query.Where(product => product.Category != null &&
                product.Category.IsActive &&
                product.Category.Name.ToLower() == categoryTerm);
        }

        if (minPrice.HasValue) query = query.Where(product => product.Price >= minPrice.Value);
        if (maxPrice.HasValue) query = query.Where(product => product.Price <= maxPrice.Value);
        if (available.HasValue) query = query.Where(product => (product.StockQuantity > 0) == available.Value);

        var totalItems = await query.CountAsync(ct);
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
        var descending = string.Equals(sortOrder, "desc", StringComparison.OrdinalIgnoreCase);

        query = sortBy?.ToLowerInvariant() switch
        {
            "name" => descending
                ? query.OrderByDescending(product => product.Name).ThenBy(product => product.Id)
                : query.OrderBy(product => product.Name).ThenBy(product => product.Id),
            "price" => descending
                ? query.OrderByDescending(product => product.Price).ThenBy(product => product.Id)
                : query.OrderBy(product => product.Price).ThenBy(product => product.Id),
            _ => query.OrderBy(product => product.Name).ThenBy(product => product.Id)
        };

        var products = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(product => new ProductResponse
            {
                Id = product.Id,
                CategoryId = product.CategoryId,
                Name = product.Name,
                Description = product.Description,
                Price = product.Price,
                Category = product.Category != null ? product.Category.Name : null,
                StockQuantity = product.StockQuantity,
                Unit = product.Unit,
                ImageUrl = product.ImageUrl,
                Available = product.StockQuantity > 0
            })
            .ToListAsync(ct);

        return new ProductListResponse
        {
            Items = products,
            TotalItems = totalItems,
            TotalPages = totalPages,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<ProductResponse> CreateProductAsync(ProductRequest request, CancellationToken ct = default)
    {
        await ValidateCategoryAsync(request.CategoryId, ct);

        var product = new Product
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow
        };

        ApplyRequest(product, request);
        await HandleImageUploadAsync(product, request, ct);

        _db.Products.Add(product);
        await _db.SaveChangesAsync(ct);

        await _db.Entry(product).Reference(item => item.Category).LoadAsync(ct);
        _logger.LogInformation("Created product {ProductId} named {ProductName}", product.Id, product.Name);
        return MapToResponse(product);
    }

    public async Task<ProductResponse?> UpdateProductAsync(Guid id, ProductRequest request, CancellationToken ct = default)
    {
        var product = await _db.Products
            .Include(item => item.Category)
            .FirstOrDefaultAsync(item => item.Id == id && item.IsActive, ct);

        if (product == null) return null;

        await ValidateCategoryAsync(request.CategoryId, ct);

        ApplyRequest(product, request);
        await HandleImageUploadAsync(product, request, ct);
        
        product.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _db.Entry(product).Reference(item => item.Category).LoadAsync(ct);
        _logger.LogInformation("Updated product {ProductId}", product.Id);
        return MapToResponse(product);
    }

    public async Task<bool> DeleteProductAsync(Guid id, CancellationToken ct = default)
    {
        var product = await _db.Products.FirstOrDefaultAsync(item => item.Id == id && item.IsActive, ct);
        if (product == null) return false;

        product.IsActive = false;
        product.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted (deactivated) product {ProductId}", id);
        return true;
    }

    private async Task ValidateCategoryAsync(Guid? categoryId, CancellationToken ct)
    {
        if (!categoryId.HasValue) return;

        var categoryExists = await _db.Categories.AnyAsync(
            category => category.Id == categoryId.Value && category.IsActive, ct);

        if (!categoryExists)
        {
            throw new InvalidProductCategoryException("Category does not exist or is inactive.");
        }
    }

    private async Task HandleImageUploadAsync(Product product, ProductRequest request, CancellationToken ct)
    {
        if (request.Image == null || request.Image.Length == 0) return;

        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
        var extension = Path.GetExtension(request.Image.FileName).ToLowerInvariant();

        if (!allowedExtensions.Contains(extension))
        {
            throw new InvalidProductImageException("Invalid file type. Only JPG, PNG, GIF, and WEBP are allowed.");
        }

        if (request.Image.Length > 5 * 1024 * 1024)
        {
            throw new InvalidProductImageException("File size exceeds the 5MB limit.");
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
            await request.Image.CopyToAsync(stream, ct);
        }

        product.ImageUrl = $"/images/{fileName}";
    }

    private static void ApplyRequest(Product product, ProductRequest request)
    {
        product.CategoryId = request.CategoryId;
        product.Name = request.Name.Trim();
        product.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        product.Price = request.Price;
        product.StockQuantity = request.StockQuantity;
        product.Unit = string.IsNullOrWhiteSpace(request.Unit) ? "item" : request.Unit.Trim();
        
        // Only overwrite image URL if it was explicitly cleared (in real scenarios we might have a specific flag for this)
        // If an image is being uploaded, HandleImageUploadAsync will overwrite it anyway.
        if (request.ImageUrl != null)
        {
            product.ImageUrl = string.IsNullOrWhiteSpace(request.ImageUrl) ? null : request.ImageUrl.Trim();
        }
        else if (request.ImageUrl == "")
        {
            product.ImageUrl = null;
        }

        product.IsActive = request.IsActive;
    }

    private static ProductResponse MapToResponse(Product product) => new()
    {
        Id = product.Id,
        CategoryId = product.CategoryId,
        Name = product.Name,
        Description = product.Description,
        Price = product.Price,
        Category = product.Category != null ? product.Category.Name : null,
        StockQuantity = product.StockQuantity,
        Unit = product.Unit,
        ImageUrl = product.ImageUrl,
        Available = product.StockQuantity > 0
    };
}

public class InvalidProductCategoryException : Exception
{
    public InvalidProductCategoryException(string message) : base(message) { }
}

public class InvalidProductImageException : Exception
{
    public InvalidProductImageException(string message) : base(message) { }
}
