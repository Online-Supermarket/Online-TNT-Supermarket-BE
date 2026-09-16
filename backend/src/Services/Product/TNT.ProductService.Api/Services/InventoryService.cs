using Microsoft.EntityFrameworkCore;
using TNT.ProductService.Api.Data;
using TNT.ProductService.Api.DTOs;

namespace TNT.ProductService.Api.Services;

public class InventoryService
{
    private readonly ProductDbContext _db;
    private readonly ILogger<InventoryService> _logger;

    public InventoryService(ProductDbContext db, ILogger<InventoryService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ── GET all ──────────────────────────────────────────────────────────────────
    public async Task<InventoryListResponse> GetInventoryAsync(
        string? search,
        int page,
        int pageSize,
        bool? lowStock = null,
        CancellationToken ct = default)
    {
        var query = _db.Products.AsNoTracking().Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(term));
        }

        if (lowStock == true)
        {
            query = query.Where(p => p.StockQuantity < p.LowStockThreshold);
        }

        var totalItems = await query.CountAsync(ct);

        var products = await query
            .OrderBy(p => p.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new InventoryResponse
            {
                ProductId         = p.Id,
                ProductName       = p.Name,
                StockQuantity     = p.StockQuantity,
                LowStockThreshold = p.LowStockThreshold,
                Unit              = p.Unit,
                UpdatedAtUtc      = p.UpdatedAtUtc
            })
            .ToListAsync(ct);

        return new InventoryListResponse
        {
            TotalItems = totalItems,
            Page       = page,
            PageSize   = pageSize,
            Items      = products
        };
    }

    // ── GET by product ID ─────────────────────────────────────────────────────────
    public async Task<InventoryResponse?> GetInventoryByProductAsync(Guid productId, CancellationToken ct = default)
    {
        var product = await _db.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productId && p.IsActive, ct);

        if (product == null) return null;

        return new InventoryResponse
        {
            ProductId         = product.Id,
            ProductName       = product.Name,
            StockQuantity     = product.StockQuantity,
            LowStockThreshold = product.LowStockThreshold,
            Unit              = product.Unit,
            UpdatedAtUtc      = product.UpdatedAtUtc
        };
    }

    // ── POST — add/initialise inventory ──────────────────────────────────────────
    /// <summary>
    /// Sets initial stock for a product. Returns 409 if the product's stock
    /// has already been explicitly initialised (StockQuantity > 0).
    /// </summary>
    public async Task<InventoryResponse> AddInventoryAsync(InventoryAddRequest request, CancellationToken ct = default)
    {
        var product = await _db.Products
            .FirstOrDefaultAsync(p => p.Id == request.ProductId && p.IsActive, ct);

        if (product == null)
            throw new InventoryProductNotFoundException("Product not found or is inactive.");

        if (product.StockQuantity > 0)
            throw new InventoryConflictException(
                $"Inventory for product '{product.Name}' already exists (current stock: {product.StockQuantity}). " +
                "Use the update endpoint to change stock levels.");

        product.StockQuantity = request.StockQuantity;
        product.UpdatedAtUtc  = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Inventory initialised for product {ProductId}: stock set to {Stock}", product.Id, product.StockQuantity);

        return new InventoryResponse
        {
            ProductId         = product.Id,
            ProductName       = product.Name,
            StockQuantity     = product.StockQuantity,
            LowStockThreshold = product.LowStockThreshold,
            Unit              = product.Unit,
            UpdatedAtUtc      = product.UpdatedAtUtc
        };
    }

    // ── PUT — direct update stock ────────────────────────────────────────────────
    public async Task<InventoryResponse?> UpdateInventoryAsync(Guid productId, InventoryUpdateRequest request, CancellationToken ct = default)
    {
        var product = await _db.Products
            .FirstOrDefaultAsync(p => p.Id == productId && p.IsActive, ct);

        if (product == null) return null;

        product.StockQuantity = request.StockQuantity;
        if (request.LowStockThreshold.HasValue)
        {
            product.LowStockThreshold = request.LowStockThreshold.Value;
        }
        product.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Inventory updated for product {ProductId}: new stock {Stock}", product.Id, product.StockQuantity);

        return new InventoryResponse
        {
            ProductId         = product.Id,
            ProductName       = product.Name,
            StockQuantity     = product.StockQuantity,
            LowStockThreshold = product.LowStockThreshold,
            Unit              = product.Unit,
            UpdatedAtUtc      = product.UpdatedAtUtc
        };
    }

    // ── PATCH — atomic stock adjustment (increase / decrease / set) ──────────────
    public async Task<InventoryResponse> AdjustStockAsync(Guid productId, StockAdjustRequest request, CancellationToken ct = default)
    {
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        if (_db.Database.IsRelational())
        {
            tx = await _db.Database.BeginTransactionAsync(ct);
        }

        try
        {
            var product = await _db.Products
                .FirstOrDefaultAsync(p => p.Id == productId && p.IsActive, ct);

            if (product == null)
                throw new InventoryProductNotFoundException("Product not found or is inactive.");

            // Calculate new stock quantity
            if (!string.IsNullOrWhiteSpace(request.Type))
            {
                var type = request.Type.Trim().ToLowerInvariant();
                switch (type)
                {
                    case "increase":
                        if (!request.Quantity.HasValue || request.Quantity < 0)
                            throw new InvalidStockAdjustmentException("Quantity is required and cannot be negative for increase.");
                        product.StockQuantity += request.Quantity.Value;
                        break;

                    case "decrease":
                        if (!request.Quantity.HasValue || request.Quantity < 0)
                            throw new InvalidStockAdjustmentException("Quantity is required and cannot be negative for decrease.");
                        if (product.StockQuantity < request.Quantity.Value)
                            throw new InsufficientStockException("Insufficient stock available.");
                        product.StockQuantity -= request.Quantity.Value;
                        break;

                    case "set":
                        if (!request.Quantity.HasValue || request.Quantity < 0)
                            throw new InvalidStockAdjustmentException("Quantity is required and cannot be negative for set.");
                        product.StockQuantity = request.Quantity.Value;
                        break;

                    default:
                        throw new InvalidStockAdjustmentException($"Unknown adjustment type: '{request.Type}'. Allowed types: 'increase', 'decrease', 'set'.");
                }
            }
            else if (request.Change.HasValue)
            {
                if (request.Change.Value < 0 && product.StockQuantity < Math.Abs(request.Change.Value))
                {
                    throw new InsufficientStockException("Insufficient stock available.");
                }
                product.StockQuantity += request.Change.Value;
            }
            else if (request.Quantity.HasValue)
            {
                product.StockQuantity = request.Quantity.Value;
            }
            else if (!request.LowStockThreshold.HasValue)
            {
                throw new InvalidStockAdjustmentException("Either Type, Change, Quantity, or LowStockThreshold must be provided.");
            }

            if (request.LowStockThreshold.HasValue)
            {
                if (request.LowStockThreshold.Value < 0)
                    throw new InvalidStockAdjustmentException("Low stock threshold cannot be negative.");
                product.LowStockThreshold = request.LowStockThreshold.Value;
            }

            // Final safeguard against negative stock
            if (product.StockQuantity < 0)
            {
                throw new InsufficientStockException("Insufficient stock available.");
            }

            product.UpdatedAtUtc = DateTime.UtcNow;

            try
            {
                await _db.SaveChangesAsync(ct);
                if (tx != null)
                {
                    await tx.CommitAsync(ct);
                }
            }
            catch (DbUpdateException dbEx) when (dbEx.InnerException?.Message.Contains("ck_products_stockquantity_nonnegative", StringComparison.OrdinalIgnoreCase) == true)
            {
                throw new InsufficientStockException("Insufficient stock available.");
            }

            _logger.LogInformation("Stock adjusted for product {ProductId}: new stock {Stock}, threshold {Threshold}",
                product.Id, product.StockQuantity, product.LowStockThreshold);

            return new InventoryResponse
            {
                ProductId         = product.Id,
                ProductName       = product.Name,
                StockQuantity     = product.StockQuantity,
                LowStockThreshold = product.LowStockThreshold,
                Unit              = product.Unit,
                UpdatedAtUtc      = product.UpdatedAtUtc
            };
        }
        finally
        {
            if (tx != null)
            {
                await tx.DisposeAsync();
            }
        }
    }

    // ── DELETE — reset stock to zero ──────────────────────────────────────────────
    /// <summary>
    /// Resets a product's StockQuantity to 0 (removes its inventory record).
    /// Returns false if the product does not exist.
    /// </summary>
    public async Task<bool> DeleteInventoryAsync(Guid productId, CancellationToken ct = default)
    {
        var product = await _db.Products
            .FirstOrDefaultAsync(p => p.Id == productId && p.IsActive, ct);

        if (product == null) return false;

        product.StockQuantity = 0;
        product.UpdatedAtUtc  = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Inventory reset (deleted) for product {ProductId}", product.Id);
        return true;
    }
}

// ── Domain exceptions ─────────────────────────────────────────────────────────
public class InventoryConflictException : Exception
{
    public InventoryConflictException(string message) : base(message) { }
}

public class InventoryProductNotFoundException : Exception
{
    public InventoryProductNotFoundException(string message) : base(message) { }
}

public class InsufficientStockException : Exception
{
    public InsufficientStockException(string message) : base(message) { }
}

public class InvalidStockAdjustmentException : Exception
{
    public InvalidStockAdjustmentException(string message) : base(message) { }
}
