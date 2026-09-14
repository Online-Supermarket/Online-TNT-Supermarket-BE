using Microsoft.EntityFrameworkCore;
using TNT.ProductService.Api.Data;
using TNT.ProductService.Api.DTOs;

namespace TNT.ProductService.Api.Services;

public class ProductService
{
    private readonly ProductDbContext _db;

    public ProductService(ProductDbContext db)
    {
        _db = db;
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
}