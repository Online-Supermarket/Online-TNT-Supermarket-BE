using Microsoft.EntityFrameworkCore;
using TNT.ProductService.Api.Data;
using TNT.ProductService.Api.DTOs;
using TNT.ProductService.Api.Entities;

namespace TNT.ProductService.Api.Services;

public class StoreService
{
    private readonly ProductDbContext _db;
    private readonly ILogger<StoreService> _logger;

    public StoreService(ProductDbContext db, ILogger<StoreService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<StoreListResponse> GetStoresAsync(
        int page = 1,
        int pageSize = 10,
        string? search = null,
        bool? isActive = null,
        CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.Stores.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(store =>
                store.StoreName.ToLower().Contains(term) ||
                store.StoreCode.ToLower().Contains(term));
        }

        if (isActive.HasValue)
        {
            query = query.Where(store => store.IsActive == isActive.Value);
        }

        var total = await query.CountAsync(ct);
        var stores = await query
            .OrderBy(store => store.StoreCode)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(store => MapToResponse(store))
            .ToListAsync(ct);

        return new StoreListResponse
        {
            Items = stores,
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<StoreResponse?> GetStoreAsync(Guid id, CancellationToken ct = default)
    {
        var store = await _db.Stores.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id, ct);

        return store == null ? null : MapToResponse(store);
    }

    public async Task<StoreResponse> CreateStoreAsync(StoreRequest request, CancellationToken ct = default)
    {
        if (await StoreCodeExistsAsync(request.StoreCode, excludeStoreId: null, ct))
        {
            throw new StoreConflictException("A store with this store code already exists.");
        }

        var store = new Store
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow
        };

        ApplyRequest(store, request);
        _db.Stores.Add(store);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created store {StoreId} with code {StoreCode}", store.Id, store.StoreCode);
        return MapToResponse(store);
    }

    public async Task<StoreResponse?> UpdateStoreAsync(Guid id, StoreRequest request, CancellationToken ct = default)
    {
        var store = await _db.Stores.FirstOrDefaultAsync(item => item.Id == id, ct);
        if (store == null) return null;

        if (await StoreCodeExistsAsync(request.StoreCode, excludeStoreId: id, ct))
        {
            throw new StoreConflictException("A store with this store code already exists.");
        }

        ApplyRequest(store, request);
        store.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Updated store {StoreId}", store.Id);
        return MapToResponse(store);
    }

    public async Task<StoreResponse?> ActivateStoreAsync(Guid id, CancellationToken ct = default)
    {
        return await SetStoreStatusAsync(id, true, ct);
    }

    public async Task<StoreResponse?> DeactivateStoreAsync(Guid id, CancellationToken ct = default)
    {
        return await SetStoreStatusAsync(id, false, ct);
    }

    private async Task<StoreResponse?> SetStoreStatusAsync(Guid id, bool isActive, CancellationToken ct)
    {
        var store = await _db.Stores.FirstOrDefaultAsync(item => item.Id == id, ct);
        if (store == null) return null;

        store.IsActive = isActive;
        store.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("{Action} store {StoreId}", isActive ? "Activated" : "Deactivated", store.Id);
        return MapToResponse(store);
    }

    private async Task<bool> StoreCodeExistsAsync(string storeCode, Guid? excludeStoreId, CancellationToken ct)
    {
        var normalizedCode = storeCode.Trim().ToLower();
        return await _db.Stores.AnyAsync(store =>
            store.StoreCode.ToLower() == normalizedCode &&
            (!excludeStoreId.HasValue || store.Id != excludeStoreId.Value), ct);
    }

    private static void ApplyRequest(Store store, StoreRequest request)
    {
        store.StoreCode = request.StoreCode.Trim();
        store.StoreName = request.StoreName.Trim();
        store.AddressLine1 = request.Address.Line1.Trim();
        store.AddressLine2 = string.IsNullOrWhiteSpace(request.Address.Line2) ? null : request.Address.Line2.Trim();
        store.City = request.Address.City.Trim();
        store.PostalCode = request.Address.PostalCode.Trim();
        store.Country = request.Address.Country.Trim();
        store.ContactNumber = request.ContactNumber.Trim();
        store.Email = request.Email.Trim();
        store.IsActive = request.IsActive;
    }

    private static StoreResponse MapToResponse(Store store) => new()
    {
        Id = store.Id,
        StoreCode = store.StoreCode,
        StoreName = store.StoreName,
        Address = new StoreAddressDto
        {
            Line1 = store.AddressLine1,
            Line2 = store.AddressLine2,
            City = store.City,
            PostalCode = store.PostalCode,
            Country = store.Country
        },
        ContactNumber = store.ContactNumber,
        Email = store.Email,
        IsActive = store.IsActive,
        CreatedAtUtc = store.CreatedAtUtc,
        UpdatedAtUtc = store.UpdatedAtUtc
    };
}

public class StoreConflictException : Exception
{
    public StoreConflictException(string message) : base(message) { }
}
