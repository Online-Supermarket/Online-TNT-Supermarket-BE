using System.Globalization;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Npgsql;
using NpgsqlTypes;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("Catalog") ?? "Host=localhost;Port=5432;Database=marketflow;Username=marketflow;Password=marketflow;Search Path=catalog"));
builder.Services.AddHttpClient<IdentityClient>(c => c.BaseAddress = new Uri(builder.Configuration["Services:IdentityUrl"] ?? "http://localhost:8081"));
builder.Services.AddSingleton<RequestMetrics>();
builder.Services.AddHostedService<OutboxPublisher>();
var app = builder.Build();
app.Use(async (context, next) => { var correlation = context.Request.Headers["X-Correlation-Id"].FirstOrDefault() ?? Guid.NewGuid().ToString(); context.Response.Headers["X-Correlation-Id"] = correlation; var stopwatch = System.Diagnostics.Stopwatch.StartNew(); using (app.Logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = correlation })) { try { await next(); } finally { context.RequestServices.GetRequiredService<RequestMetrics>().Record(context.Response.StatusCode, stopwatch.Elapsed); } } });
var applyMigrations = app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("Migrations:ApplyOnStartup");
if (applyMigrations)
    await CatalogDb.InitializeAsync(app.Services.GetRequiredService<NpgsqlDataSource>(), app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("Seed:DemoData"));
if (applyMigrations && builder.Configuration.GetValue<bool>("Migrations:ExitAfterApply")) return;

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "catalog" }));
app.MapGet("/health/ready", async (NpgsqlDataSource db, IConfiguration cfg) => await DependencyHealth.Ready(db, cfg, "catalog"));
app.MapGet("/metrics", async (RequestMetrics m, NpgsqlDataSource db) => Results.Text(m.AsPrometheus("catalog", await CatalogDb.OperationalMetrics(db)), "text/plain"));
app.MapGet("/openapi/v1.json", () => Results.Text(OpenApi.Document("MarketFlow Catalog API", new("get", "/health", "Liveness check"), new("get", "/health/ready", "Dependency readiness check"), new("get", "/categories", "List categories"), new("get", "/categories/{id}", "Get category by ID"), new("post", "/categories", "Create a category"), new("put", "/categories/{id}", "Update a category"), new("delete", "/categories/{id}", "Delete a category"), new("get", "/products", "Browse products"), new("post", "/products", "Create a product"), new("put", "/products/{id}", "Update a product"), new("delete", "/products/{id}", "Deactivate a product"), new("get", "/reports/inventory", "Run the inventory report"), new("get", "/reports/inventory/export", "Export the inventory report"), new("get", "/inventory", "Get all inventory items with search & status filters"), new("get", "/inventory/summary", "Get inventory summary metrics"), new("get", "/inventory/{id}", "Get inventory item by product ID"), new("post", "/inventory/{id}/add", "Add stock to product"), new("post", "/inventory/{id}/remove", "Remove stock from product"), new("put", "/inventory/{id}/adjust", "Adjust stock quantity directly"), new("get", "/inventory/history", "Get stock movement history ledger")), "application/json"));
app.MapGet("/swagger", () => Results.Content(OpenApi.Ui, "text/html"));
app.MapGet("/categories", CatalogDb.Categories);
app.MapGet("/categories/{id:guid}", CatalogDb.Category);
app.MapPost("/categories", async (CategoryInput input, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) =>
{
    var actor = await auth.Principal(req, "Staff", "Admin"); if (actor is null) return Results.StatusCode(403);
    var errors = CategoryInput.Validate(input); if (errors.Count > 0) return Results.ValidationProblem(errors);
    try
    {
        var category = await CatalogDb.CreateCategory(db, input);
        return Results.Created($"/categories/{category.Id}", category);
    }
    catch (PostgresException e) when (e.SqlState == "23505")
    {
        return Results.Conflict(new { message = "Category name already exists." });
    }
});
app.MapPut("/categories/{id:guid}", async (Guid id, CategoryInput input, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) =>
{
    var actor = await auth.Principal(req, "Staff", "Admin"); if (actor is null) return Results.StatusCode(403);
    var errors = CategoryInput.Validate(input); if (errors.Count > 0) return Results.ValidationProblem(errors);
    try
    {
        var category = await CatalogDb.UpdateCategory(db, id, input);
        return category is null ? Results.NotFound() : Results.Ok(category);
    }
    catch (PostgresException e) when (e.SqlState == "23505")
    {
        return Results.Conflict(new { message = "Category name already exists." });
    }
});
app.MapDelete("/categories/{id:guid}", async (Guid id, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) =>
{
    var actor = await auth.Principal(req, "Staff", "Admin"); if (actor is null) return Results.StatusCode(403);
    try
    {
        return await CatalogDb.DeleteCategory(db, id) ? Results.NoContent() : Results.NotFound();
    }
    catch (PostgresException e) when (e.SqlState == "23503")
    {
        return Results.Conflict(new { message = "Cannot delete category because existing products reference it." });
    }
});
app.MapGet("/products", CatalogDb.Products);
app.MapGet("/products/{id:guid}", CatalogDb.Product);
app.MapGet("/staff/products", async (HttpRequest req, IdentityClient auth, NpgsqlDataSource db) => !await auth.Allowed(req, "Staff", "Admin") ? Results.StatusCode(403) : Results.Ok(await CatalogDb.ReadProducts(db, true)));
app.MapPost("/products", async (ProductInput input, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) =>
{
    var actor = await auth.Principal(req, "Staff", "Admin"); if (actor is null) return Results.StatusCode(403);
    var errors = ProductInput.Validate(input); if (errors.Count > 0) return Results.ValidationProblem(errors);
    try { var id = await CatalogDb.UpsertProduct(db, null, input, actor.Subject, CorrelationId.From(req)); return Results.Created($"/products/{id}", new { id }); }
    catch (PostgresException e) when (e.SqlState == "23505") { return Results.Conflict(new { message = "SKU already exists." }); }
});
app.MapPut("/products/{id:guid}", async (Guid id, ProductInput input, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) =>
{
    var actor = await auth.Principal(req, "Staff", "Admin"); if (actor is null) return Results.StatusCode(403);
    var errors = ProductInput.Validate(input); if (errors.Count > 0) return Results.ValidationProblem(errors);
    try { return await CatalogDb.UpsertProduct(db, id, input, actor.Subject, CorrelationId.From(req)) == Guid.Empty ? Results.NotFound() : Results.NoContent(); }
    catch (PostgresException e) when (e.SqlState == "23505") { return Results.Conflict(new { message = "SKU already exists." }); }
});
app.MapMethods("/products/{id:guid}/deactivate", ["PATCH"], async (Guid id, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) =>
{
    var actor = await auth.Principal(req, "Staff", "Admin"); if (actor is null) return Results.StatusCode(403);
    return await CatalogDb.Deactivate(db, id, actor.Subject, CorrelationId.From(req)) ? Results.NoContent() : Results.NotFound();
});
app.MapDelete("/products/{id:guid}", async (Guid id, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) =>
{
    // Only Admin may permanently delete a product from the database.
    var actor = await auth.Principal(req, "Admin"); if (actor is null) return Results.StatusCode(403);
    return await CatalogDb.DeleteProduct(db, id) ? Results.NoContent() : Results.NotFound();
});
app.MapGet("/reports/inventory", async (Guid? categoryId, int? threshold, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) =>
    !await auth.Allowed(req, "Staff", "Admin") ? Results.StatusCode(403) : Results.Ok(await CatalogDb.Inventory(db, categoryId, threshold ?? 5)));
app.MapGet("/reports/inventory/export", async (Guid? categoryId, int? threshold, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) =>
{
    if (!await auth.Allowed(req, "Staff", "Admin")) return Results.StatusCode(403);
    var report = await CatalogDb.Inventory(db, categoryId, threshold ?? 5); var csv = new StringBuilder("SKU,Name,Category,Price,Stock Quantity,Low Stock,Active\n");
    foreach (var row in report.Products) csv.Append(Csv.Escape(row.Sku)).Append(',').Append(Csv.Escape(row.Name)).Append(',').Append(Csv.Escape(row.CategoryName)).Append(',').Append(row.Price.ToString("0.00", CultureInfo.InvariantCulture)).Append(',').Append(row.StockQuantity).Append(',').Append(row.LowStock).Append(',').Append(row.Active).Append('\n');
    return Results.File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", "inventory-report.csv");
});
app.MapPost("/internal/stock-reservations", async (ReservationRequest request, HttpRequest req, IConfiguration cfg, NpgsqlDataSource db) =>
{
    if (!InternalAuth.Valid(req, cfg)) return Results.StatusCode(401);
    if (request.OrderId == Guid.Empty || request.Lines.Count == 0 || request.Lines.Any(x => x.ProductId == Guid.Empty || x.Quantity <= 0)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["lines"] = ["An order ID and positive product quantities are required."] });
    return Results.Ok(await CatalogDb.Reserve(db, request));
});
app.MapGet("/internal/stock-reservations/by-order/{orderId:guid}", async (Guid orderId, HttpRequest req, IConfiguration cfg, NpgsqlDataSource db) => !InternalAuth.Valid(req, cfg) ? Results.StatusCode(401) : Results.Ok(await CatalogDb.Reservation(db, orderId)));
app.MapPost("/internal/stock-reservations/{orderId:guid}/release", async (Guid orderId, ReleaseRequest request, HttpRequest req, IConfiguration cfg, NpgsqlDataSource db) => !InternalAuth.Valid(req, cfg) ? Results.StatusCode(401) : Results.Ok(await CatalogDb.Release(db, orderId, request.CorrelationId == Guid.Empty ? CorrelationId.From(req) : request.CorrelationId)));

// ── Inventory Replenishment ───────────────────────────────────────────────────
app.MapGet("/inventory/low-stock", async (HttpRequest req, IdentityClient auth, NpgsqlDataSource db) =>
{
    var actor = await auth.Principal(req, "Staff", "Admin"); if (actor is null) return Results.StatusCode(403);
    return Results.Ok(await CatalogDb.LowStock(db));
});
app.MapGet("/inventory/replenishment", async (string? status, Guid? productId, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) =>
{
    var actor = await auth.Principal(req, "Staff", "Admin"); if (actor is null) return Results.StatusCode(403);
    return Results.Ok(await CatalogDb.ReplenishmentPlans(db, status, productId));
});
app.MapGet("/inventory/replenishment/{id:guid}", async (Guid id, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) =>
{
    var actor = await auth.Principal(req, "Staff", "Admin"); if (actor is null) return Results.StatusCode(403);
    var plan = await CatalogDb.ReplenishmentPlan(db, id); return plan is null ? Results.NotFound() : Results.Ok(plan);
});
app.MapPost("/inventory/replenishment", async (ReplenishmentInput input, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) =>
{
    var actor = await auth.Principal(req, "Staff", "Admin"); if (actor is null) return Results.StatusCode(403);
    if (input.ProductId == Guid.Empty) return Results.ValidationProblem(new Dictionary<string, string[]> { ["productId"] = ["ProductId is required."] });
    if (input.RequestedQuantity <= 0) return Results.ValidationProblem(new Dictionary<string, string[]> { ["requestedQuantity"] = ["RequestedQuantity must be greater than 0."] });
    var plan = await CatalogDb.CreateReplenishmentPlan(db, input, actor.Subject);
    return plan is null ? Results.NotFound(new { message = "Product not found." }) : Results.Created($"/inventory/replenishment/{plan.Id}", plan);
});
app.MapMethods("/inventory/replenishment/{id:guid}/approve", ["PATCH"], async (Guid id, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) =>
{
    var actor = await auth.Principal(req, "Staff", "Admin"); if (actor is null) return Results.StatusCode(403);
    return await CatalogDb.TransitionPlan(db, id, "Pending", "Approved", actor.Subject) switch { "ok" => Results.NoContent(), "notfound" => Results.NotFound(), _ => Results.Conflict(new { message = "Plan must be Pending to approve." }) };
});
app.MapMethods("/inventory/replenishment/{id:guid}/ordered", ["PATCH"], async (Guid id, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) =>
{
    var actor = await auth.Principal(req, "Staff", "Admin"); if (actor is null) return Results.StatusCode(403);
    return await CatalogDb.TransitionPlan(db, id, "Approved", "Ordered", actor.Subject) switch { "ok" => Results.NoContent(), "notfound" => Results.NotFound(), _ => Results.Conflict(new { message = "Plan must be Approved to mark as Ordered." }) };
});
app.MapMethods("/inventory/replenishment/{id:guid}/cancel", ["PATCH"], async (Guid id, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) =>
{
    var actor = await auth.Principal(req, "Staff", "Admin"); if (actor is null) return Results.StatusCode(403);
    return await CatalogDb.CancelPlan(db, id, actor.Subject) switch { "ok" => Results.NoContent(), "notfound" => Results.NotFound(), _ => Results.Conflict(new { message = "Only Pending or Approved plans can be cancelled." }) };
});
app.MapMethods("/inventory/replenishment/{id:guid}/receive", ["PATCH"], async (Guid id, ReceiveStockInput input, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) =>
{
    var actor = await auth.Principal(req, "Staff", "Admin"); if (actor is null) return Results.StatusCode(403);
    if (input.ReceivedQuantity <= 0) return Results.ValidationProblem(new Dictionary<string, string[]> { ["receivedQuantity"] = ["ReceivedQuantity must be greater than 0."] });
    var result = await CatalogDb.ReceiveStock(db, id, input, actor.Subject, CorrelationId.From(req));
    return result switch { "ok" => Results.NoContent(), "notfound" => Results.NotFound(), "already_received" => Results.Conflict(new { message = "Stock for this plan has already been received." }), _ => Results.Conflict(new { message = "Plan must be in Approved or Ordered status to receive stock." }) };
});

// ── Inventory Stock Management ────────────────────────────────────────────────
app.MapGet("/inventory", async (string? search, Guid? categoryId, string? status, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) =>
{
    var actor = await auth.Principal(req, "Staff", "Admin");
    if (actor is null) return Results.StatusCode(403);
    return Results.Ok(await CatalogDb.AllInventory(db, search, categoryId, status));
});

app.MapGet("/inventory/summary", async (HttpRequest req, IdentityClient auth, NpgsqlDataSource db) =>
{
    var actor = await auth.Principal(req, "Staff", "Admin");
    if (actor is null) return Results.StatusCode(403);
    return Results.Ok(await CatalogDb.InventorySummary(db));
});

app.MapGet("/inventory/{id:guid}", async (Guid id, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) =>
{
    var actor = await auth.Principal(req, "Staff", "Admin");
    if (actor is null) return Results.StatusCode(403);
    var item = await CatalogDb.ProductInventory(db, id);
    return item is null ? Results.NotFound() : Results.Ok(item);
});

app.MapPost("/inventory/{id:guid}/add", async (Guid id, AddStockInput input, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) =>
{
    var actor = await auth.Principal(req, "Staff", "Admin");
    if (actor is null) return Results.StatusCode(403);
    if (input.Quantity <= 0)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["quantity"] = ["Quantity must be greater than 0."] });
    var (success, code, updated) = await CatalogDb.AddStock(db, id, input, actor.Subject, CorrelationId.From(req));
    return code switch
    {
        "ok" => Results.Ok(updated),
        "notfound" => Results.NotFound(new { message = "Product not found." }),
        _ => Results.BadRequest(new { message = "Failed to add stock." })
    };
});

app.MapPost("/inventory/{id:guid}/remove", async (Guid id, RemoveStockInput input, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) =>
{
    var actor = await auth.Principal(req, "Staff", "Admin");
    if (actor is null) return Results.StatusCode(403);
    if (input.Quantity <= 0)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["quantity"] = ["Quantity must be greater than 0."] });
    var (success, code, updated) = await CatalogDb.RemoveStock(db, id, input, actor.Subject, CorrelationId.From(req));
    return code switch
    {
        "ok" => Results.Ok(updated),
        "notfound" => Results.NotFound(new { message = "Product not found." }),
        "insufficient_stock" => Results.BadRequest(new { message = "Insufficient stock available to remove." }),
        _ => Results.BadRequest(new { message = "Failed to remove stock." })
    };
});

app.MapPut("/inventory/{id:guid}/adjust", async (Guid id, AdjustStockInput input, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) =>
{
    var actor = await auth.Principal(req, "Staff", "Admin");
    if (actor is null) return Results.StatusCode(403);
    var errors = new Dictionary<string, string[]>();
    if (input.NewQuantity < 0)
        errors["newQuantity"] = ["NewQuantity must be greater than or equal to 0."];
    if (string.IsNullOrWhiteSpace(input.Reason))
        errors["reason"] = ["Reason is required when directly adjusting stock."];
    if (errors.Count > 0)
        return Results.ValidationProblem(errors);

    var (success, code, updated) = await CatalogDb.AdjustStock(db, id, input, actor.Subject, CorrelationId.From(req));
    return code switch
    {
        "ok" => Results.Ok(updated),
        "notfound" => Results.NotFound(new { message = "Product not found." }),
        "invalid_quantity" => Results.BadRequest(new { message = "New stock quantity cannot be negative." }),
        _ => Results.BadRequest(new { message = "Failed to adjust stock." })
    };
});

app.MapGet("/inventory/history", async (Guid? productId, string? adjustmentType, int? limit, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) =>
{
    var actor = await auth.Principal(req, "Staff", "Admin");
    if (actor is null) return Results.StatusCode(403);
    return Results.Ok(await CatalogDb.StockHistory(db, productId, adjustmentType, limit ?? 100));
});

app.Run();

record CategoryInput(string Name, string? Description, string? ImageUrl)
{
    public static Dictionary<string, string[]> Validate(CategoryInput c)
    {
        var e = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(c.Name)) e["name"] = ["Name is required."];
        if (c.Name?.Trim().Length > 100) e["name"] = ["Name cannot exceed 100 characters."];
        if (c.Description?.Trim().Length > 500) e["description"] = ["Description cannot exceed 500 characters."];
        if (c.ImageUrl?.Trim().Length > 2000) e["imageUrl"] = ["Image URL cannot exceed 2000 characters."];
        return e;
    }
}
record CategoryDto(Guid Id, string Name, string? Description, string? ImageUrl);

record ProductInput(string Sku, string Name, string? Description, decimal Price, int StockQuantity, Guid CategoryId, string? ImageUrl = null, int ReorderLevel = 10, int TargetStockLevel = 50)
{
    public static Dictionary<string, string[]> Validate(ProductInput p) { var e = new Dictionary<string, string[]>(); if (string.IsNullOrWhiteSpace(p.Sku)) e["sku"] = ["SKU is required."]; if (string.IsNullOrWhiteSpace(p.Name)) e["name"] = ["Name is required."]; if (p.Price < 0) e["price"] = ["Price cannot be negative."]; if (p.StockQuantity < 0) e["stockQuantity"] = ["Stock cannot be negative."]; if (p.CategoryId == Guid.Empty) e["categoryId"] = ["Category is required."]; if (p.ImageUrl?.Trim().Length > 2000) e["imageUrl"] = ["Image URL cannot exceed 2000 characters."]; if (p.ReorderLevel < 0) e["reorderLevel"] = ["Reorder level cannot be negative."]; if (p.TargetStockLevel <= p.ReorderLevel) e["targetStockLevel"] = ["Target stock level must be greater than reorder level."]; return e; }
}

record ReplenishmentInput(Guid ProductId, int RequestedQuantity, string? Notes);
record ReceiveStockInput(int ReceivedQuantity, string? Notes);
record ReplenishmentPlanDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string ProductSku,
    string CategoryName,
    int CurrentStock,
    int ReorderLevel,
    int TargetStockLevel,
    int SuggestedQuantity,
    int RequestedQuantity,
    string Status,
    string? Notes,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    Guid? ApprovedBy = null,
    DateTimeOffset? ApprovedAt = null,
    Guid? ReceivedBy = null,
    DateTimeOffset? ReceivedAt = null,
    int? ReceivedQuantity = null
);

record AddStockInput(int Quantity, string? Notes);
record RemoveStockInput(int Quantity, string? Reason, string? Notes);
record AdjustStockInput(int NewQuantity, string Reason, string? Notes);

record InventoryProductDto(
    Guid Id,
    string Sku,
    string Name,
    string? Description,
    decimal Price,
    int StockQuantity,
    int ReorderLevel,
    int TargetStockLevel,
    DateTimeOffset? LastRestockedAt,
    string Status,
    Guid CategoryId,
    string CategoryName,
    bool Active,
    string? ImageUrl,
    DateTimeOffset UpdatedAt
);

record InventorySummaryDto(
    int TotalProducts,
    int InStockCount,
    int LowStockCount,
    int OutOfStockCount,
    long TotalStockUnits
);

record StockMovementDto(
    long Id,
    Guid ProductId,
    string ProductName,
    string ProductSku,
    int QuantityDelta,
    string Reason,
    string? AdjustmentType,
    int? PreviousQuantity,
    int? NewQuantity,
    string? Notes,
    Guid? ActorId,
    Guid CorrelationId,
    DateTimeOffset OccurredAt
);

record Principal(Guid Subject, string[] Roles);
record ReservationLine(Guid ProductId, int Quantity);
record ReservationRequest(Guid OrderId, List<ReservationLine> Lines, Guid CorrelationId);
record ReleaseRequest(Guid CorrelationId);
record ReservationResult(Guid OrderId, Guid? ReservationId, string State, List<object> Lines);
record InventoryRow(string Sku, string Name, string CategoryName, decimal Price, int StockQuantity, bool LowStock, bool Active);
record InventoryReport(int TotalProducts, int LowStockCount, int ZeroStockCount, DateTimeOffset GeneratedAt, List<InventoryRow> Products);
record ProductSnapshot(Guid Id, string Sku, string Name, decimal Price, int StockQuantity, bool Active, Guid CategoryId, string? ImageUrl = null);

static class CorrelationId { public static Guid From(HttpRequest request) => Guid.TryParse(request.Headers["X-Correlation-Id"].FirstOrDefault(), out var id) ? id : Guid.NewGuid(); }
static class InternalAuth { public static bool Valid(HttpRequest request, IConfiguration config) => config["Internal:Key"] is { Length: > 0 } key && request.Headers["X-Internal-Key"].FirstOrDefault() == key; }
static class Csv { public static string Escape(string value) => $"\"{value.Replace("\"", "\"\"")}\""; }

sealed class IdentityClient(HttpClient http)
{
    public async Task<bool> Allowed(HttpRequest request, params string[] roles) => await Principal(request, roles) is not null;
    public async Task<Principal?> Principal(HttpRequest request, params string[] roles)
    {
        var token = request.Headers.Authorization.FirstOrDefault(); if (string.IsNullOrWhiteSpace(token)) return null;
        using var message = new HttpRequestMessage(HttpMethod.Get, "/auth/introspect"); message.Headers.TryAddWithoutValidation("Authorization", token);
        try { var result = await http.SendAsync(message); if (!result.IsSuccessStatusCode) return null; var root = JsonDocument.Parse(await result.Content.ReadAsStringAsync()).RootElement; if (!root.GetProperty("active").GetBoolean()) return null; var found = root.GetProperty("roles").EnumerateArray().Select(x => x.GetString()!).ToArray(); return roles.Any(found.Contains) ? new Principal(root.GetProperty("subject").GetGuid(), found) : null; } catch { return null; }
    }
}

static class CatalogDb
{
    public static async Task InitializeAsync(NpgsqlDataSource db, bool seedDemoData)
    {
        await using (var history = db.CreateCommand("CREATE SCHEMA IF NOT EXISTS catalog; CREATE TABLE IF NOT EXISTS catalog.schema_migrations(version text PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now());")) await history.ExecuteNonQueryAsync();
        await using (var ensureCols = db.CreateCommand("ALTER TABLE catalog.categories ADD COLUMN IF NOT EXISTS description text NULL; ALTER TABLE catalog.categories ADD COLUMN IF NOT EXISTS image_url text NULL; ALTER TABLE catalog.products ADD COLUMN IF NOT EXISTS image_url text NULL; ALTER TABLE catalog.products ADD COLUMN IF NOT EXISTS reorder_level integer NOT NULL DEFAULT 10; ALTER TABLE catalog.products ADD COLUMN IF NOT EXISTS target_stock_level integer NOT NULL DEFAULT 50; ALTER TABLE catalog.products ADD COLUMN IF NOT EXISTS last_restocked_at timestamptz NULL;")) { try { await ensureCols.ExecuteNonQueryAsync(); } catch { /* columns may already exist */ } }
        await using (var ensureCascade = db.CreateCommand("DO $$ BEGIN ALTER TABLE catalog.replenishment_plans DROP CONSTRAINT IF EXISTS replenishment_plans_product_id_fkey; ALTER TABLE catalog.replenishment_plans ADD CONSTRAINT replenishment_plans_product_id_fkey FOREIGN KEY (product_id) REFERENCES catalog.products(id) ON DELETE CASCADE; ALTER TABLE catalog.stock_reservation_items DROP CONSTRAINT IF EXISTS stock_reservation_items_product_id_fkey; ALTER TABLE catalog.stock_reservation_items ADD CONSTRAINT stock_reservation_items_product_id_fkey FOREIGN KEY (product_id) REFERENCES catalog.products(id) ON DELETE CASCADE; EXCEPTION WHEN OTHERS THEN NULL; END $$;")) { try { await ensureCascade.ExecuteNonQueryAsync(); } catch { /* ignore */ } }

        // Migration 001_baseline
        await using (var conn = await db.OpenConnectionAsync())
        await using (var tx = await conn.BeginTransactionAsync())
        {
            await using var claim = new NpgsqlCommand("INSERT INTO catalog.schema_migrations(version) VALUES('001_baseline') ON CONFLICT DO NOTHING RETURNING version", conn, tx);
            if (await claim.ExecuteScalarAsync() is not null)
            {
                var sql = await MigrationSql.BaselineAsync();
                await using var cmd = new NpgsqlCommand(sql, conn, tx);
                await cmd.ExecuteNonQueryAsync();
                if (seedDemoData) foreach (var category in new[] { ("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "Fruit & Vegetables"), ("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", "Pantry") }) { await using var seed = new NpgsqlCommand("INSERT INTO catalog.categories(id,name) VALUES($1,$2) ON CONFLICT DO NOTHING", conn, tx); seed.Parameters.AddWithValue(Guid.Parse(category.Item1)); seed.Parameters.AddWithValue(category.Item2); await seed.ExecuteNonQueryAsync(); }
            }
            await tx.CommitAsync();
        }

        // Migration 004_replenishment
        await using (var conn = await db.OpenConnectionAsync())
        await using (var tx = await conn.BeginTransactionAsync())
        {
            await using var claim = new NpgsqlCommand("INSERT INTO catalog.schema_migrations(version) VALUES('004_replenishment') ON CONFLICT DO NOTHING RETURNING version", conn, tx);
            if (await claim.ExecuteScalarAsync() is not null)
            {
                var sql = await MigrationSql.ReplenishmentAsync();
                await using var cmd = new NpgsqlCommand(sql, conn, tx);
                await cmd.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
        }

        // Migration 005_stock_management
        await using (var conn = await db.OpenConnectionAsync())
        await using (var tx = await conn.BeginTransactionAsync())
        {
            await using var claim = new NpgsqlCommand("INSERT INTO catalog.schema_migrations(version) VALUES('005_stock_management') ON CONFLICT DO NOTHING RETURNING version", conn, tx);
            if (await claim.ExecuteScalarAsync() is not null)
            {
                var sql = await MigrationSql.StockManagementAsync();
                await using var cmd = new NpgsqlCommand(sql, conn, tx);
                await cmd.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
        }
    }
    public static async Task<OperationalMetrics> OperationalMetrics(NpgsqlDataSource db) { await using var cmd = db.CreateCommand("SELECT (SELECT count(*) FROM catalog.outbox WHERE published_at IS NULL), (SELECT count(*) FROM catalog.stock_reservations WHERE state='Failed')"); await using var reader = await cmd.ExecuteReaderAsync(); await reader.ReadAsync(); return new OperationalMetrics(reader.GetInt64(0), reader.GetInt64(1)); }
    public static async Task<IResult> Categories(NpgsqlDataSource db)
    {
        await using var cmd = db.CreateCommand("SELECT id, name, description, image_url FROM catalog.categories ORDER BY name");
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<CategoryDto>();
        while (await reader.ReadAsync())
        {
            rows.Add(new CategoryDto(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)
            ));
        }
        return Results.Ok(rows);
    }

    public static async Task<IResult> Category(Guid id, NpgsqlDataSource db)
    {
        await using var cmd = db.CreateCommand("SELECT id, name, description, image_url FROM catalog.categories WHERE id = $1");
        cmd.Parameters.AddWithValue(id);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return Results.NotFound();
        return Results.Ok(new CategoryDto(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3)
        ));
    }

    public static async Task<CategoryDto> CreateCategory(NpgsqlDataSource db, CategoryInput input)
    {
        var id = Guid.NewGuid();
        await using var conn = await db.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("INSERT INTO catalog.categories(id, name, description, image_url) VALUES($1, $2, $3, $4)", conn);
        cmd.Parameters.AddWithValue(id);
        cmd.Parameters.AddWithValue(input.Name.Trim());
        cmd.Parameters.AddWithValue((object?)input.Description?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)input.ImageUrl?.Trim() ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
        return new CategoryDto(id, input.Name.Trim(), input.Description?.Trim(), input.ImageUrl?.Trim());
    }

    public static async Task<CategoryDto?> UpdateCategory(NpgsqlDataSource db, Guid id, CategoryInput input)
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("UPDATE catalog.categories SET name = $2, description = $3, image_url = $4 WHERE id = $1", conn);
        cmd.Parameters.AddWithValue(id);
        cmd.Parameters.AddWithValue(input.Name.Trim());
        cmd.Parameters.AddWithValue((object?)input.Description?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)input.ImageUrl?.Trim() ?? DBNull.Value);
        var rowsAffected = await cmd.ExecuteNonQueryAsync();
        if (rowsAffected == 0) return null;
        return new CategoryDto(id, input.Name.Trim(), input.Description?.Trim(), input.ImageUrl?.Trim());
    }

    public static async Task<bool> DeleteCategory(NpgsqlDataSource db, Guid id)
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM catalog.categories WHERE id = $1", conn);
        cmd.Parameters.AddWithValue(id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }
    public static async Task<bool> DeleteProduct(NpgsqlDataSource db, Guid id)
    {
        await using var conn = await db.OpenConnectionAsync();
        // Also clean up related replenishment_plans, stock_reservation_items, audit_log and stock_movements rows so FK constraints don't block
        await using var tx = await conn.BeginTransactionAsync();
        await using var del0a = new NpgsqlCommand("DELETE FROM catalog.replenishment_plans WHERE product_id = $1", conn, tx);
        del0a.Parameters.AddWithValue(id); await del0a.ExecuteNonQueryAsync();
        await using var del0b = new NpgsqlCommand("DELETE FROM catalog.stock_reservation_items WHERE product_id = $1", conn, tx);
        del0b.Parameters.AddWithValue(id); await del0b.ExecuteNonQueryAsync();
        await using var del1 = new NpgsqlCommand("DELETE FROM catalog.audit_log WHERE product_id = $1", conn, tx);
        del1.Parameters.AddWithValue(id); await del1.ExecuteNonQueryAsync();
        await using var del2 = new NpgsqlCommand("DELETE FROM catalog.stock_movements WHERE product_id = $1", conn, tx);
        del2.Parameters.AddWithValue(id); await del2.ExecuteNonQueryAsync();
        await using var del3 = new NpgsqlCommand("DELETE FROM catalog.products WHERE id = $1", conn, tx);
        del3.Parameters.AddWithValue(id); var affected = await del3.ExecuteNonQueryAsync();
        await tx.CommitAsync();
        return affected > 0;
    }
    public static async Task<IResult> Products(string? q, Guid? categoryId, int? page, int? pageSize, NpgsqlDataSource db)
    {
        var p = Math.Max(1, page ?? 1);
        var ps = Math.Clamp(pageSize ?? 20, 1, 100);
        await using var cmd = db.CreateCommand("SELECT p.id,p.sku,p.name,p.description,p.price,p.stock_quantity,p.active,c.id,c.name,p.image_url,p.reorder_level,p.target_stock_level,p.last_restocked_at FROM products p JOIN categories c ON c.id=p.category_id WHERE p.active=true AND ($1 IS NULL OR p.name ILIKE '%'||$1||'%' OR p.sku ILIKE '%'||$1||'%') AND ($2 IS NULL OR p.category_id=$2) ORDER BY p.name OFFSET $3 LIMIT $4");
        cmd.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)q?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue(NpgsqlDbType.Uuid, (object?)categoryId ?? DBNull.Value);
        cmd.Parameters.AddWithValue((p - 1) * ps);
        cmd.Parameters.AddWithValue(ps);
        return Results.Ok(await ReadProducts(cmd));
    }

    public static async Task<IResult> Product(Guid id, NpgsqlDataSource db) { await using var cmd = db.CreateCommand("SELECT p.id,p.sku,p.name,p.description,p.price,p.stock_quantity,p.active,c.id,c.name,p.image_url,p.reorder_level,p.target_stock_level,p.last_restocked_at FROM products p JOIN categories c ON c.id=p.category_id WHERE p.id=$1 AND p.active=true"); cmd.Parameters.AddWithValue(id); var item = (await ReadProducts(cmd)).SingleOrDefault(); return item is null ? Results.NotFound() : Results.Ok(item); }
    public static async Task<List<object>> ReadProducts(NpgsqlDataSource db, bool all) { await using var cmd = db.CreateCommand($"SELECT p.id,p.sku,p.name,p.description,p.price,p.stock_quantity,p.active,c.id,c.name,p.image_url,p.reorder_level,p.target_stock_level,p.last_restocked_at FROM products p JOIN categories c ON c.id=p.category_id {(all ? "" : "WHERE p.active=true")} ORDER BY p.name"); return await ReadProducts(cmd); }
    static async Task<List<object>> ReadProducts(NpgsqlCommand cmd) { await using var reader = await cmd.ExecuteReaderAsync(); var rows = new List<object>(); while (await reader.ReadAsync()) rows.Add(new { id = reader.GetGuid(0), sku = reader.GetString(1), name = reader.GetString(2), description = reader.IsDBNull(3) ? null : reader.GetString(3), price = reader.GetDecimal(4), stockQuantity = reader.GetInt32(5), available = reader.GetInt32(5) > 0, active = reader.GetBoolean(6), category = new { id = reader.GetGuid(7), name = reader.GetString(8) }, imageUrl = reader.IsDBNull(9) ? null : reader.GetString(9), reorderLevel = reader.GetInt32(10), targetStockLevel = reader.GetInt32(11), lastRestockedAt = reader.IsDBNull(12) ? (DateTimeOffset?)null : reader.GetDateTime(12) }); return rows; }
    public static async Task<Guid> UpsertProduct(NpgsqlDataSource db, Guid? id, ProductInput input, Guid actor, Guid correlation)
    {
        await using var conn = await db.OpenConnectionAsync(); await using var tx = await conn.BeginTransactionAsync(); var value = id ?? Guid.NewGuid(); int? priorStock = null;
        if (id is not null) { await using var prior = new NpgsqlCommand("SELECT stock_quantity FROM catalog.products WHERE id=$1 FOR UPDATE", conn, tx); prior.Parameters.AddWithValue(value); var priorValue = await prior.ExecuteScalarAsync(); if (priorValue is null) return Guid.Empty; priorStock = Convert.ToInt32(priorValue, CultureInfo.InvariantCulture); }
        await using var write = new NpgsqlCommand(id is null ? "INSERT INTO catalog.products(id,sku,name,description,price,stock_quantity,category_id,created_by,updated_by,image_url,reorder_level,target_stock_level) VALUES($1,$2,$3,$4,$5,$6,$7,$8,$8,$9,$10,$11)" : "UPDATE catalog.products SET sku=$2,name=$3,description=$4,price=$5,stock_quantity=$6,category_id=$7,updated_by=$8,image_url=$9,reorder_level=$10,target_stock_level=$11,updated_at=now() WHERE id=$1", conn, tx);
        write.Parameters.AddWithValue(value); write.Parameters.AddWithValue(input.Sku.Trim()); write.Parameters.AddWithValue(input.Name.Trim()); write.Parameters.AddWithValue((object?)input.Description?.Trim() ?? DBNull.Value); write.Parameters.AddWithValue(input.Price); write.Parameters.AddWithValue(input.StockQuantity); write.Parameters.AddWithValue(input.CategoryId); write.Parameters.AddWithValue(actor); write.Parameters.AddWithValue((object?)input.ImageUrl?.Trim() ?? DBNull.Value);
        write.Parameters.AddWithValue(input.ReorderLevel); write.Parameters.AddWithValue(input.TargetStockLevel);
        await write.ExecuteNonQueryAsync();
        var snapshot = await Snapshot(conn, tx, value); await AuditAndOutbox(conn, tx, snapshot!, "ProductChanged", actor, correlation);
        if (priorStock is not null && priorStock != input.StockQuantity) await StockMovement(conn, tx, value, input.StockQuantity - priorStock.Value, "ManualAdjustment", actor, correlation);
        await tx.CommitAsync(); return value;
    }
    public static async Task<bool> Deactivate(NpgsqlDataSource db, Guid id, Guid actor, Guid correlation)
    {
        await using var conn = await db.OpenConnectionAsync(); await using var tx = await conn.BeginTransactionAsync(); await using var write = new NpgsqlCommand("UPDATE catalog.products SET active=false,updated_by=$2,updated_at=now() WHERE id=$1 AND active=true", conn, tx); write.Parameters.AddWithValue(id); write.Parameters.AddWithValue(actor); if (await write.ExecuteNonQueryAsync() == 0) return false;
        await AuditAndOutbox(conn, tx, (await Snapshot(conn, tx, id))!, "ProductChanged", actor, correlation); await tx.CommitAsync(); return true;
    }
    static async Task<ProductSnapshot?> Snapshot(NpgsqlConnection conn, NpgsqlTransaction tx, Guid id) { await using var cmd = new NpgsqlCommand("SELECT id,sku,name,price,stock_quantity,active,category_id,image_url FROM catalog.products WHERE id=$1", conn, tx); cmd.Parameters.AddWithValue(id); await using var reader = await cmd.ExecuteReaderAsync(); return await reader.ReadAsync() ? new ProductSnapshot(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetDecimal(3), reader.GetInt32(4), reader.GetBoolean(5), reader.GetGuid(6), reader.IsDBNull(7) ? null : reader.GetString(7)) : null; }
    static async Task AuditAndOutbox(NpgsqlConnection conn, NpgsqlTransaction tx, ProductSnapshot product, string type, Guid actor, Guid correlation)
    {
        var eventId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new
        {
            eventId,
            schemaVersion = 1,
            eventType = type,
            occurredAt = DateTimeOffset.UtcNow,
            correlationId = correlation,
            productId = product.Id,
            sku = product.Sku,
            name = product.Name,
            price = product.Price,
            stockQuantity = product.StockQuantity,
            active = product.Active,
            categoryId = product.CategoryId,
            imageUrl = product.ImageUrl
        });

        await using (var audit = new NpgsqlCommand("INSERT INTO catalog.audit_log(product_id,action,actor_id) VALUES($1,$2,$3)", conn, tx))
        {
            audit.Parameters.AddWithValue(product.Id);
            audit.Parameters.AddWithValue(type);
            audit.Parameters.AddWithValue(actor);
            await audit.ExecuteNonQueryAsync();
        }

        await using var outbox = new NpgsqlCommand("INSERT INTO catalog.outbox(event_id,event_type,payload) VALUES($1,$2,CAST($3 AS jsonb))", conn, tx);
        outbox.Parameters.AddWithValue(eventId);
        outbox.Parameters.AddWithValue(type);
        outbox.Parameters.AddWithValue(payload);
        await outbox.ExecuteNonQueryAsync();
    }

    static async Task StockMovement(NpgsqlConnection conn, NpgsqlTransaction tx, Guid productId, int delta, string reason, Guid? actor, Guid correlation) { await using var cmd = new NpgsqlCommand("INSERT INTO catalog.stock_movements(product_id,quantity_delta,reason,actor_id,correlation_id) VALUES($1,$2,$3,$4,$5)", conn, tx); cmd.Parameters.AddWithValue(productId); cmd.Parameters.AddWithValue(delta); cmd.Parameters.AddWithValue(reason); cmd.Parameters.AddWithValue((object?)actor ?? DBNull.Value); cmd.Parameters.AddWithValue(correlation); await cmd.ExecuteNonQueryAsync(); }

    public static async Task<List<object>> LowStock(NpgsqlDataSource db)
    {
        await using var cmd = db.CreateCommand("""
            SELECT p.id, p.sku, p.name, c.name AS category_name, p.price, p.stock_quantity,
                   p.reorder_level, p.target_stock_level,
                   GREATEST(0, p.target_stock_level - p.stock_quantity) AS suggested_quantity,
                   p.image_url, p.last_restocked_at,
                   (
                       SELECT rp.status FROM catalog.replenishment_plans rp
                       WHERE rp.product_id = p.id AND rp.status IN ('Pending', 'Approved', 'Ordered')
                       ORDER BY rp.created_at DESC LIMIT 1
                   ) AS active_plan_status
            FROM catalog.products p
            JOIN catalog.categories c ON c.id = p.category_id
            WHERE p.active = true AND p.stock_quantity <= p.reorder_level
            ORDER BY (p.reorder_level - p.stock_quantity) DESC, p.stock_quantity ASC, p.name ASC
        """);
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<object>();
        while (await reader.ReadAsync())
        {
            rows.Add(new
            {
                id = reader.GetGuid(0),
                sku = reader.GetString(1),
                name = reader.GetString(2),
                categoryName = reader.GetString(3),
                price = reader.GetDecimal(4),
                stockQuantity = reader.GetInt32(5),
                reorderLevel = reader.GetInt32(6),
                targetStockLevel = reader.GetInt32(7),
                suggestedQuantity = reader.GetInt32(8),
                imageUrl = reader.IsDBNull(9) ? null : reader.GetString(9),
                lastRestockedAt = reader.IsDBNull(10) ? (DateTimeOffset?)null : reader.GetDateTime(10),
                activePlanStatus = reader.IsDBNull(11) ? null : reader.GetString(11)
            });
        }
        return rows;
    }

    public static async Task<List<ReplenishmentPlanDto>> ReplenishmentPlans(NpgsqlDataSource db, string? status, Guid? productId)
    {
        await using var cmd = db.CreateCommand("""
            SELECT rp.id, rp.product_id, p.name AS product_name, p.sku AS product_sku, c.name AS category_name,
                   rp.current_stock, rp.reorder_level, rp.target_stock_level, rp.suggested_quantity,
                   rp.requested_quantity, rp.status, rp.notes,
                   rp.created_by, rp.created_at, rp.approved_by, rp.approved_at,
                   rp.received_by, rp.received_at, rp.received_quantity
            FROM catalog.replenishment_plans rp
            JOIN catalog.products p ON p.id = rp.product_id
            JOIN catalog.categories c ON c.id = p.category_id
            WHERE ($1 IS NULL OR rp.status = $1) AND ($2 IS NULL OR rp.product_id = $2)
            ORDER BY rp.created_at DESC
        """);
        cmd.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)status?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue(NpgsqlDbType.Uuid, (object?)productId ?? DBNull.Value);
        return await ReadReplenishmentPlans(cmd);
    }

    public static async Task<ReplenishmentPlanDto?> ReplenishmentPlan(NpgsqlDataSource db, Guid id)
    {
        await using var cmd = db.CreateCommand("""
            SELECT rp.id, rp.product_id, p.name AS product_name, p.sku AS product_sku, c.name AS category_name,
                   rp.current_stock, rp.reorder_level, rp.target_stock_level, rp.suggested_quantity,
                   rp.requested_quantity, rp.status, rp.notes,
                   rp.created_by, rp.created_at, rp.approved_by, rp.approved_at,
                   rp.received_by, rp.received_at, rp.received_quantity
            FROM catalog.replenishment_plans rp
            JOIN catalog.products p ON p.id = rp.product_id
            JOIN catalog.categories c ON c.id = p.category_id
            WHERE rp.id = $1
        """);
        cmd.Parameters.AddWithValue(id);
        var list = await ReadReplenishmentPlans(cmd);
        return list.FirstOrDefault();
    }

    static async Task<List<ReplenishmentPlanDto>> ReadReplenishmentPlans(NpgsqlCommand cmd)
    {
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<ReplenishmentPlanDto>();
        while (await reader.ReadAsync())
        {
            rows.Add(new ReplenishmentPlanDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.GetInt32(8),
                reader.GetInt32(9),
                reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.GetGuid(12),
                reader.GetDateTime(13),
                reader.IsDBNull(14) ? (Guid?)null : reader.GetGuid(14),
                reader.IsDBNull(15) ? (DateTimeOffset?)null : reader.GetDateTime(15),
                reader.IsDBNull(16) ? (Guid?)null : reader.GetGuid(16),
                reader.IsDBNull(17) ? (DateTimeOffset?)null : reader.GetDateTime(17),
                reader.IsDBNull(18) ? (int?)null : reader.GetInt32(18)
            ));
        }
        return rows;
    }

    public static async Task<ReplenishmentPlanDto?> CreateReplenishmentPlan(NpgsqlDataSource db, ReplenishmentInput input, Guid actor)
    {
        await using var conn = await db.OpenConnectionAsync();
        int stockQuantity = 0, reorderLevel = 10, targetStockLevel = 50;
        string productName = "", productSku = "", categoryName = "";
        await using (var getProd = new NpgsqlCommand("SELECT p.name, p.sku, c.name, p.stock_quantity, p.reorder_level, p.target_stock_level FROM catalog.products p JOIN catalog.categories c ON c.id=p.category_id WHERE p.id=$1", conn))
        {
            getProd.Parameters.AddWithValue(input.ProductId);
            await using var reader = await getProd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;
            productName = reader.GetString(0);
            productSku = reader.GetString(1);
            categoryName = reader.GetString(2);
            stockQuantity = reader.GetInt32(3);
            reorderLevel = reader.GetInt32(4);
            targetStockLevel = reader.GetInt32(5);
        }

        var id = Guid.NewGuid();
        var suggested = Math.Max(0, targetStockLevel - stockQuantity);
        var now = DateTimeOffset.UtcNow;

        await using (var insert = new NpgsqlCommand("""
            INSERT INTO catalog.replenishment_plans(id, product_id, current_stock, reorder_level, target_stock_level, suggested_quantity, requested_quantity, status, notes, created_by, created_at)
            VALUES($1, $2, $3, $4, $5, $6, $7, 'Pending', $8, $9, $10)
        """, conn))
        {
            insert.Parameters.AddWithValue(id);
            insert.Parameters.AddWithValue(input.ProductId);
            insert.Parameters.AddWithValue(stockQuantity);
            insert.Parameters.AddWithValue(reorderLevel);
            insert.Parameters.AddWithValue(targetStockLevel);
            insert.Parameters.AddWithValue(suggested);
            insert.Parameters.AddWithValue(input.RequestedQuantity);
            insert.Parameters.AddWithValue((object?)input.Notes?.Trim() ?? DBNull.Value);
            insert.Parameters.AddWithValue(actor);
            insert.Parameters.AddWithValue(now);
            await insert.ExecuteNonQueryAsync();
        }

        return new ReplenishmentPlanDto(
            id,
            input.ProductId,
            productName,
            productSku,
            categoryName,
            stockQuantity,
            reorderLevel,
            targetStockLevel,
            suggested,
            input.RequestedQuantity,
            "Pending",
            input.Notes?.Trim(),
            actor,
            now
        );
    }

    public static async Task<string> TransitionPlan(NpgsqlDataSource db, Guid id, string requiredStatus, string newStatus, Guid actor)
    {
        await using var conn = await db.OpenConnectionAsync();
        string currentStatus;
        await using (var check = new NpgsqlCommand("SELECT status FROM catalog.replenishment_plans WHERE id=$1", conn))
        {
            check.Parameters.AddWithValue(id);
            var val = await check.ExecuteScalarAsync();
            if (val is null) return "notfound";
            currentStatus = (string)val;
        }

        if (!string.Equals(currentStatus, requiredStatus, StringComparison.OrdinalIgnoreCase)) return "invalid_status";

        if (newStatus == "Approved")
        {
            await using var cmd = new NpgsqlCommand("UPDATE catalog.replenishment_plans SET status=$1, approved_by=$2, approved_at=now() WHERE id=$3", conn);
            cmd.Parameters.AddWithValue(newStatus);
            cmd.Parameters.AddWithValue(actor);
            cmd.Parameters.AddWithValue(id);
            await cmd.ExecuteNonQueryAsync();
        }
        else
        {
            await using var cmd = new NpgsqlCommand("UPDATE catalog.replenishment_plans SET status=$1 WHERE id=$2", conn);
            cmd.Parameters.AddWithValue(newStatus);
            cmd.Parameters.AddWithValue(id);
            await cmd.ExecuteNonQueryAsync();
        }
        return "ok";
    }

    public static async Task<string> CancelPlan(NpgsqlDataSource db, Guid id, Guid actor)
    {
        await using var conn = await db.OpenConnectionAsync();
        string currentStatus;
        await using (var check = new NpgsqlCommand("SELECT status FROM catalog.replenishment_plans WHERE id=$1", conn))
        {
            check.Parameters.AddWithValue(id);
            var val = await check.ExecuteScalarAsync();
            if (val is null) return "notfound";
            currentStatus = (string)val;
        }

        if (currentStatus is not ("Pending" or "Approved")) return "invalid_status";

        await using var cmd = new NpgsqlCommand("UPDATE catalog.replenishment_plans SET status='Cancelled' WHERE id=$1", conn);
        cmd.Parameters.AddWithValue(id);
        await cmd.ExecuteNonQueryAsync();
        return "ok";
    }

    public static async Task<string> ReceiveStock(NpgsqlDataSource db, Guid id, ReceiveStockInput input, Guid actor, Guid correlation)
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        Guid productId;
        string currentStatus;
        await using (var check = new NpgsqlCommand("SELECT product_id, status FROM catalog.replenishment_plans WHERE id=$1 FOR UPDATE", conn, tx))
        {
            check.Parameters.AddWithValue(id);
            await using var reader = await check.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return "notfound";
            productId = reader.GetGuid(0);
            currentStatus = reader.GetString(1);
        }

        if (currentStatus == "Received") return "already_received";
        if (currentStatus is not ("Approved" or "Ordered")) return "invalid_status";

        await using (var updateProd = new NpgsqlCommand("UPDATE catalog.products SET stock_quantity = stock_quantity + $1, last_restocked_at = now(), updated_at = now() WHERE id = $2", conn, tx))
        {
            updateProd.Parameters.AddWithValue(input.ReceivedQuantity);
            updateProd.Parameters.AddWithValue(productId);
            await updateProd.ExecuteNonQueryAsync();
        }

        await using (var updatePlan = new NpgsqlCommand("UPDATE catalog.replenishment_plans SET status='Received', received_by=$1, received_at=now(), received_quantity=$2 WHERE id=$3", conn, tx))
        {
            updatePlan.Parameters.AddWithValue(actor);
            updatePlan.Parameters.AddWithValue(input.ReceivedQuantity);
            updatePlan.Parameters.AddWithValue(id);
            await updatePlan.ExecuteNonQueryAsync();
        }

        await StockMovement(conn, tx, productId, input.ReceivedQuantity, "Replenishment", actor, correlation);

        var eventId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new
        {
            eventId,
            schemaVersion = 1,
            eventType = "StockReplenished",
            occurredAt = DateTimeOffset.UtcNow,
            correlationId = correlation,
            productId,
            replenishmentPlanId = id,
            receivedQuantity = input.ReceivedQuantity,
            actorId = actor
        });
        await using (var outbox = new NpgsqlCommand("INSERT INTO catalog.outbox(event_id,event_type,payload) VALUES($1,$2,CAST($3 AS jsonb))", conn, tx))
        {
            outbox.Parameters.AddWithValue(eventId);
            outbox.Parameters.AddWithValue("StockReplenished");
            outbox.Parameters.AddWithValue(payload);
            await outbox.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        return "ok";
    }

    public static async Task<List<InventoryProductDto>> AllInventory(NpgsqlDataSource db, string? search, Guid? categoryId, string? status)
    {
        var sql = """
            SELECT p.id, p.sku, p.name, p.description, p.price, p.stock_quantity, p.reorder_level, p.target_stock_level,
                   p.last_restocked_at, c.id, c.name, p.active, p.image_url, p.updated_at
            FROM catalog.products p
            JOIN catalog.categories c ON c.id = p.category_id
            WHERE p.active = true
              AND ($1 IS NULL OR p.name ILIKE '%' || $1 || '%' OR p.sku ILIKE '%' || $1 || '%')
              AND ($2 IS NULL OR p.category_id = $2)
            ORDER BY p.name ASC
        """;
        await using var cmd = db.CreateCommand(sql);
        cmd.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)search?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue(NpgsqlDbType.Uuid, (object?)categoryId ?? DBNull.Value);
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<InventoryProductDto>();
        while (await reader.ReadAsync())
        {
            var stock = reader.GetInt32(5);
            var reorder = reader.GetInt32(6);
            var itemStatus = stock == 0 ? "Out of Stock" : (stock <= reorder ? "Low Stock" : "In Stock");
            if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status.Trim(), itemStatus, StringComparison.OrdinalIgnoreCase))
                continue;

            rows.Add(new InventoryProductDto(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetDecimal(4),
                stock,
                reorder,
                reader.GetInt32(7),
                reader.IsDBNull(8) ? (DateTimeOffset?)null : reader.GetDateTime(8),
                itemStatus,
                reader.GetGuid(9),
                reader.GetString(10),
                reader.GetBoolean(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.GetDateTime(13)
            ));
        }
        return rows;
    }

    public static async Task<InventorySummaryDto> InventorySummary(NpgsqlDataSource db)
    {
        var sql = """
            SELECT 
                count(*)::int AS total_products,
                count(*) FILTER (WHERE stock_quantity > reorder_level)::int AS in_stock_count,
                count(*) FILTER (WHERE stock_quantity > 0 AND stock_quantity <= reorder_level)::int AS low_stock_count,
                count(*) FILTER (WHERE stock_quantity = 0)::int AS out_of_stock_count,
                coalesce(sum(stock_quantity), 0)::bigint AS total_stock_units
            FROM catalog.products
            WHERE active = true
        """;
        await using var cmd = db.CreateCommand(sql);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new InventorySummaryDto(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt64(4)
            );
        }
        return new InventorySummaryDto(0, 0, 0, 0, 0);
    }

    public static async Task<InventoryProductDto?> ProductInventory(NpgsqlDataSource db, Guid id)
    {
        var sql = """
            SELECT p.id, p.sku, p.name, p.description, p.price, p.stock_quantity, p.reorder_level, p.target_stock_level,
                   p.last_restocked_at, c.id, c.name, p.active, p.image_url, p.updated_at
            FROM catalog.products p
            JOIN catalog.categories c ON c.id = p.category_id
            WHERE p.id = $1
        """;
        await using var cmd = db.CreateCommand(sql);
        cmd.Parameters.AddWithValue(id);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        var stock = reader.GetInt32(5);
        var reorder = reader.GetInt32(6);
        var itemStatus = stock == 0 ? "Out of Stock" : (stock <= reorder ? "Low Stock" : "In Stock");
        return new InventoryProductDto(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetDecimal(4),
            stock,
            reorder,
            reader.GetInt32(7),
            reader.IsDBNull(8) ? (DateTimeOffset?)null : reader.GetDateTime(8),
            itemStatus,
            reader.GetGuid(9),
            reader.GetString(10),
            reader.GetBoolean(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.GetDateTime(13)
        );
    }

    public static async Task<(bool Success, string Code, InventoryProductDto? Product)> AddStock(NpgsqlDataSource db, Guid id, AddStockInput input, Guid actor, Guid correlation)
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        int currentStock;
        await using (var check = new NpgsqlCommand("SELECT stock_quantity FROM catalog.products WHERE id = $1 FOR UPDATE", conn, tx))
        {
            check.Parameters.AddWithValue(id);
            var val = await check.ExecuteScalarAsync();
            if (val is null) return (false, "notfound", null);
            currentStock = (int)val;
        }

        var newStock = currentStock + input.Quantity;
        await using (var update = new NpgsqlCommand("UPDATE catalog.products SET stock_quantity = $1, last_restocked_at = now(), updated_at = now() WHERE id = $2", conn, tx))
        {
            update.Parameters.AddWithValue(newStock);
            update.Parameters.AddWithValue(id);
            await update.ExecuteNonQueryAsync();
        }

        await RecordStockMovement(conn, tx, id, input.Quantity, "Stock Addition", actor, correlation, currentStock, newStock, "Add", input.Notes);
        await OutboxStockAdjusted(conn, tx, id, input.Quantity, currentStock, newStock, "Add", "Stock Addition", input.Notes, actor, correlation);

        await tx.CommitAsync();
        var updated = await ProductInventory(db, id);
        return (true, "ok", updated);
    }

    public static async Task<(bool Success, string Code, InventoryProductDto? Product)> RemoveStock(NpgsqlDataSource db, Guid id, RemoveStockInput input, Guid actor, Guid correlation)
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        int currentStock;
        await using (var check = new NpgsqlCommand("SELECT stock_quantity FROM catalog.products WHERE id = $1 FOR UPDATE", conn, tx))
        {
            check.Parameters.AddWithValue(id);
            var val = await check.ExecuteScalarAsync();
            if (val is null) return (false, "notfound", null);
            currentStock = (int)val;
        }

        if (currentStock < input.Quantity)
        {
            return (false, "insufficient_stock", null);
        }

        var newStock = currentStock - input.Quantity;
        await using (var update = new NpgsqlCommand("UPDATE catalog.products SET stock_quantity = $1, updated_at = now() WHERE id = $2", conn, tx))
        {
            update.Parameters.AddWithValue(newStock);
            update.Parameters.AddWithValue(id);
            await update.ExecuteNonQueryAsync();
        }

        var reason = string.IsNullOrWhiteSpace(input.Reason) ? "Stock Removal" : input.Reason.Trim();
        await RecordStockMovement(conn, tx, id, -input.Quantity, reason, actor, correlation, currentStock, newStock, "Remove", input.Notes);
        await OutboxStockAdjusted(conn, tx, id, -input.Quantity, currentStock, newStock, "Remove", reason, input.Notes, actor, correlation);

        await tx.CommitAsync();
        var updated = await ProductInventory(db, id);
        return (true, "ok", updated);
    }

    public static async Task<(bool Success, string Code, InventoryProductDto? Product)> AdjustStock(NpgsqlDataSource db, Guid id, AdjustStockInput input, Guid actor, Guid correlation)
    {
        if (input.NewQuantity < 0) return (false, "invalid_quantity", null);

        await using var conn = await db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        int currentStock;
        await using (var check = new NpgsqlCommand("SELECT stock_quantity FROM catalog.products WHERE id = $1 FOR UPDATE", conn, tx))
        {
            check.Parameters.AddWithValue(id);
            var val = await check.ExecuteScalarAsync();
            if (val is null) return (false, "notfound", null);
            currentStock = (int)val;
        }

        var newStock = input.NewQuantity;
        var delta = newStock - currentStock;
        await using (var update = new NpgsqlCommand("UPDATE catalog.products SET stock_quantity = $1, updated_at = now() WHERE id = $2", conn, tx))
        {
            update.Parameters.AddWithValue(newStock);
            update.Parameters.AddWithValue(id);
            await update.ExecuteNonQueryAsync();
        }

        var reason = input.Reason.Trim();
        await RecordStockMovement(conn, tx, id, delta, reason, actor, correlation, currentStock, newStock, "Adjust", input.Notes);
        await OutboxStockAdjusted(conn, tx, id, delta, currentStock, newStock, "Adjust", reason, input.Notes, actor, correlation);

        await tx.CommitAsync();
        var updated = await ProductInventory(db, id);
        return (true, "ok", updated);
    }

    public static async Task<List<StockMovementDto>> StockHistory(NpgsqlDataSource db, Guid? productId, string? adjustmentType, int limit)
    {
        limit = Math.Clamp(limit, 1, 500);
        var sql = """
            SELECT sm.id, sm.product_id, p.name AS product_name, p.sku AS product_sku,
                   sm.quantity_delta, sm.reason, sm.adjustment_type, sm.previous_quantity,
                   sm.new_quantity, sm.notes, sm.actor_id, sm.correlation_id, sm.occurred_at
            FROM catalog.stock_movements sm
            JOIN catalog.products p ON p.id = sm.product_id
            WHERE ($1 IS NULL OR sm.product_id = $1)
              AND ($2 IS NULL OR sm.adjustment_type = $2)
            ORDER BY sm.occurred_at DESC
            LIMIT $3
        """;
        await using var cmd = db.CreateCommand(sql);
        cmd.Parameters.AddWithValue(NpgsqlDbType.Uuid, (object?)productId ?? DBNull.Value);
        cmd.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)adjustmentType?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue(limit);
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<StockMovementDto>();
        while (await reader.ReadAsync())
        {
            rows.Add(new StockMovementDto(
                reader.GetInt64(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? (int?)null : reader.GetInt32(7),
                reader.IsDBNull(8) ? (int?)null : reader.GetInt32(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? (Guid?)null : reader.GetGuid(10),
                reader.GetGuid(11),
                reader.GetDateTime(12)
            ));
        }
        return rows;
    }

    static async Task RecordStockMovement(NpgsqlConnection conn, NpgsqlTransaction tx, Guid productId, int delta, string reason, Guid? actor, Guid correlation, int? prevQty = null, int? newQty = null, string? adjType = null, string? notes = null)
    {
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO catalog.stock_movements(product_id, quantity_delta, reason, actor_id, correlation_id, occurred_at, previous_quantity, new_quantity, adjustment_type, notes)
            VALUES($1, $2, $3, $4, $5, now(), $6, $7, $8, $9)
        """, conn, tx);
        cmd.Parameters.AddWithValue(productId);
        cmd.Parameters.AddWithValue(delta);
        cmd.Parameters.AddWithValue(reason);
        cmd.Parameters.AddWithValue((object?)actor ?? DBNull.Value);
        cmd.Parameters.AddWithValue(correlation);
        cmd.Parameters.AddWithValue((object?)prevQty ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)newQty ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)adjType ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)notes?.Trim() ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    static async Task OutboxStockAdjusted(NpgsqlConnection conn, NpgsqlTransaction tx, Guid productId, int delta, int? prevQty, int? newQty, string? adjType, string reason, string? notes, Guid actor, Guid correlation)
    {
        var eventId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new
        {
            eventId,
            schemaVersion = 1,
            eventType = "StockAdjusted",
            occurredAt = DateTimeOffset.UtcNow,
            correlationId = correlation,
            productId,
            delta,
            previousQuantity = prevQty,
            newQuantity = newQty,
            adjustmentType = adjType,
            reason,
            notes,
            actorId = actor
        });
        await using var cmd = new NpgsqlCommand("INSERT INTO catalog.outbox(event_id,event_type,payload) VALUES($1,$2,CAST($3 AS jsonb))", conn, tx);
        cmd.Parameters.AddWithValue(eventId);
        cmd.Parameters.AddWithValue("StockAdjusted");
        cmd.Parameters.AddWithValue(payload);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<InventoryReport> Inventory(NpgsqlDataSource db, Guid? categoryId, int threshold)
    {
        threshold = Math.Max(0, threshold);
        await using var cmd = db.CreateCommand("SELECT p.sku,p.name,c.name,p.price,p.stock_quantity,p.active FROM catalog.products p JOIN catalog.categories c ON c.id=p.category_id WHERE p.active=true AND ($1 IS NULL OR p.category_id=$1) ORDER BY p.stock_quantity,p.name");
        cmd.Parameters.AddWithValue(NpgsqlDbType.Uuid, (object?)categoryId ?? DBNull.Value);
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<InventoryRow>();
        while (await reader.ReadAsync())
        {
            var stock = reader.GetInt32(4);
            rows.Add(new InventoryRow(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetDecimal(3), stock, stock <= threshold, reader.GetBoolean(5)));
        }
        return new InventoryReport(rows.Count, rows.Count(x => x.LowStock), rows.Count(x => x.StockQuantity == 0), DateTimeOffset.UtcNow, rows);
    }

    public static async Task<ReservationResult> Reserve(NpgsqlDataSource db, ReservationRequest request)
    {
        await using var conn = await db.OpenConnectionAsync(); await using var tx = await conn.BeginTransactionAsync();
        await using (var exists = new NpgsqlCommand("SELECT id,state FROM catalog.stock_reservations WHERE order_id=$1", conn, tx)) { exists.Parameters.AddWithValue(request.OrderId); await using var reader = await exists.ExecuteReaderAsync(); if (await reader.ReadAsync()) { var priorId = reader.GetGuid(0); var priorState = reader.GetString(1); await reader.CloseAsync(); var priorLines = await ReservationLines(conn, tx, priorId); await tx.CommitAsync(); return Result(request.OrderId, priorId, priorState, priorLines); } }
        var lines = request.Lines.GroupBy(x => x.ProductId).Select(g => new ReservationLine(g.Key, g.Sum(x => x.Quantity))).OrderBy(x => x.ProductId).ToList(); var failures = new List<object>();
        foreach (var line in lines) { await using var check = new NpgsqlCommand("SELECT stock_quantity,active FROM catalog.products WHERE id=$1 FOR UPDATE", conn, tx); check.Parameters.AddWithValue(line.ProductId); await using var reader = await check.ExecuteReaderAsync(); var found = await reader.ReadAsync(); var available = found ? reader.GetInt32(0) : 0; var active = found && reader.GetBoolean(1); if (!active || available < line.Quantity) failures.Add(new { productId = line.ProductId, requested = line.Quantity, available }); }
        var reservationId = Guid.NewGuid(); var state = failures.Count == 0 ? "Reserved" : "Failed"; var correlation = request.CorrelationId == Guid.Empty ? Guid.NewGuid() : request.CorrelationId;
        await using (var insert = new NpgsqlCommand("INSERT INTO catalog.stock_reservations(id,order_id,state,correlation_id) VALUES($1,$2,$3,$4)", conn, tx)) { insert.Parameters.AddWithValue(reservationId); insert.Parameters.AddWithValue(request.OrderId); insert.Parameters.AddWithValue(state); insert.Parameters.AddWithValue(correlation); await insert.ExecuteNonQueryAsync(); }
        if (failures.Count > 0) { await ReservationOutbox(conn, tx, "StockReservationFailed", request.OrderId, reservationId, correlation, failures); await tx.CommitAsync(); return new ReservationResult(request.OrderId, reservationId, state, failures); }
        foreach (var line in lines) { await using var update = new NpgsqlCommand("UPDATE catalog.products SET stock_quantity=stock_quantity-$2,updated_at=now() WHERE id=$1", conn, tx); update.Parameters.AddWithValue(line.ProductId); update.Parameters.AddWithValue(line.Quantity); await update.ExecuteNonQueryAsync(); await using var item = new NpgsqlCommand("INSERT INTO catalog.stock_reservation_items(reservation_id,product_id,quantity) VALUES($1,$2,$3)", conn, tx); item.Parameters.AddWithValue(reservationId); item.Parameters.AddWithValue(line.ProductId); item.Parameters.AddWithValue(line.Quantity); await item.ExecuteNonQueryAsync(); await StockMovement(conn, tx, line.ProductId, -line.Quantity, "Reservation", null, correlation); }
        await ReservationOutbox(conn, tx, "StockReserved", request.OrderId, reservationId, correlation, lines); await tx.CommitAsync(); return Result(request.OrderId, reservationId, state, lines);
    }
    public static async Task<object?> Reservation(NpgsqlDataSource db, Guid orderId) { await using var cmd = db.CreateCommand("SELECT id,state FROM stock_reservations WHERE order_id=$1"); cmd.Parameters.AddWithValue(orderId); await using var reader = await cmd.ExecuteReaderAsync(); return await reader.ReadAsync() ? new { orderId, reservationId = reader.GetGuid(0), state = reader.GetString(1) } : null; }
    public static async Task<ReservationResult> Release(NpgsqlDataSource db, Guid orderId, Guid correlation)
    {
        await using var conn = await db.OpenConnectionAsync(); await using var tx = await conn.BeginTransactionAsync(); Guid id; string state;
        await using (var find = new NpgsqlCommand("SELECT id,state FROM catalog.stock_reservations WHERE order_id=$1 FOR UPDATE", conn, tx)) { find.Parameters.AddWithValue(orderId); await using var reader = await find.ExecuteReaderAsync(); if (!await reader.ReadAsync()) { await tx.CommitAsync(); return new ReservationResult(orderId, null, "Missing", []); } id = reader.GetGuid(0); state = reader.GetString(1); }
        var lines = await ReservationLines(conn, tx, id); if (state is "Released" or "Failed") { await tx.CommitAsync(); return Result(orderId, id, state, lines); }
        foreach (var line in lines) { await using var update = new NpgsqlCommand("UPDATE catalog.products SET stock_quantity=stock_quantity+$2,updated_at=now() WHERE id=$1", conn, tx); update.Parameters.AddWithValue(line.ProductId); update.Parameters.AddWithValue(line.Quantity); await update.ExecuteNonQueryAsync(); await StockMovement(conn, tx, line.ProductId, line.Quantity, "ReservationRelease", null, correlation); }
        await using (var updateState = new NpgsqlCommand("UPDATE catalog.stock_reservations SET state='Released',updated_at=now() WHERE id=$1", conn, tx)) { updateState.Parameters.AddWithValue(id); await updateState.ExecuteNonQueryAsync(); }
        await ReservationOutbox(conn, tx, "StockReleased", orderId, id, correlation, lines); await tx.CommitAsync(); return Result(orderId, id, "Released", lines);
    }
    static ReservationResult Result(Guid orderId, Guid id, string state, List<ReservationLine> lines) => new(orderId, id, state, lines.Select(x => (object)new { productId = x.ProductId, quantity = x.Quantity }).ToList());
    static async Task<List<ReservationLine>> ReservationLines(NpgsqlConnection conn, NpgsqlTransaction tx, Guid reservationId) { await using var cmd = new NpgsqlCommand("SELECT product_id,quantity FROM catalog.stock_reservation_items WHERE reservation_id=$1", conn, tx); cmd.Parameters.AddWithValue(reservationId); await using var reader = await cmd.ExecuteReaderAsync(); var lines = new List<ReservationLine>(); while (await reader.ReadAsync()) lines.Add(new ReservationLine(reader.GetGuid(0), reader.GetInt32(1))); return lines; }
    static async Task ReservationOutbox(NpgsqlConnection conn, NpgsqlTransaction tx, string type, Guid orderId, Guid reservationId, Guid correlation, object payloadLines) { var eventId = Guid.NewGuid(); var payload = JsonSerializer.Serialize(new { eventId, schemaVersion = 1, eventType = type, occurredAt = DateTimeOffset.UtcNow, correlationId = correlation, orderId, reservationId, lines = payloadLines }); await using var cmd = new NpgsqlCommand("INSERT INTO catalog.outbox(event_id,event_type,payload) VALUES($1,$2,CAST($3 AS jsonb))", conn, tx); cmd.Parameters.AddWithValue(eventId); cmd.Parameters.AddWithValue(type); cmd.Parameters.AddWithValue(payload); await cmd.ExecuteNonQueryAsync(); }
}

sealed class OutboxPublisher(IServiceProvider services, IConfiguration config, ILogger<OutboxPublisher> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var producer = new ProducerBuilder<string, string>(KafkaClientSettings.Producer(config)).Build();
        while (!stop.IsCancellationRequested) { try { using var scope = services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>(); await using var cmd = db.CreateCommand("SELECT event_id,payload::text FROM catalog.outbox WHERE published_at IS NULL ORDER BY occurred_at LIMIT 30"); await using var reader = await cmd.ExecuteReaderAsync(stop); var rows = new List<(Guid, string)>(); while (await reader.ReadAsync(stop)) rows.Add((reader.GetGuid(0), reader.GetString(1))); await reader.CloseAsync(); foreach (var row in rows) { await producer.ProduceAsync("catalog.events", new Message<string, string> { Key = row.Item1.ToString(), Value = row.Item2 }, stop); await using var done = db.CreateCommand("UPDATE catalog.outbox SET published_at=now() WHERE event_id=$1"); done.Parameters.AddWithValue(row.Item1); await done.ExecuteNonQueryAsync(stop); } } catch (Exception ex) { log.LogWarning(ex, "Catalog outbox publish will retry"); } await Task.Delay(TimeSpan.FromSeconds(3), stop); }
    }
}
record OperationalMetrics(long OutboxPending, long FailedReservations);
static class MigrationSql { public static Task<string> BaselineAsync() => File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Migrations", "001_baseline.sql")); public static Task<string> ReplenishmentAsync() => File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Migrations", "004_replenishment.sql")); public static Task<string> StockManagementAsync() => File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Migrations", "005_stock_management.sql")); }
static class DependencyHealth { public static async Task<IResult> Ready(NpgsqlDataSource db, IConfiguration cfg, string service) { try { await using var cmd = db.CreateCommand("SELECT 1"); await cmd.ExecuteScalarAsync(); using var kafka = new AdminClientBuilder(KafkaClientSettings.Admin(cfg)).Build(); var metadata = kafka.GetMetadata(TimeSpan.FromSeconds(2)); if (metadata.Brokers.Count == 0) throw new InvalidOperationException("Kafka has no available broker."); return Results.Ok(new { status = "ready", service, dependencies = new { postgres = "ready", kafka = "ready" } }); } catch { return Results.Problem("A required dependency is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable); } } }
sealed class RequestMetrics { long _requests; long _errors; long _durationTicks; public void Record(int status, TimeSpan duration) { Interlocked.Increment(ref _requests); if (status >= 500) Interlocked.Increment(ref _errors); Interlocked.Add(ref _durationTicks, duration.Ticks); } public string AsPrometheus(string service, OperationalMetrics? operational = null) => $"# TYPE marketflow_http_requests_total counter\nmarketflow_http_requests_total{{service=\"{service}\"}} {_requests}\n# TYPE marketflow_http_errors_total counter\nmarketflow_http_errors_total{{service=\"{service}\"}} {_errors}\n# TYPE marketflow_http_request_duration_seconds summary\nmarketflow_http_request_duration_seconds_sum{{service=\"{service}\"}} {TimeSpan.FromTicks(_durationTicks).TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}\nmarketflow_http_request_duration_seconds_count{{service=\"{service}\"}} {_requests}\n# TYPE marketflow_outbox_pending gauge\nmarketflow_outbox_pending{{service=\"{service}\"}} {operational?.OutboxPending ?? 0}\n# TYPE marketflow_failed_reservations_total counter\nmarketflow_failed_reservations_total{{service=\"{service}\"}} {operational?.FailedReservations ?? 0}\n"; }
static class OpenApi { public record Route(string Method, string Path, string Summary); public static string Document(string title, params Route[] routes) => JsonSerializer.Serialize(new { openapi = "3.0.3", info = new { title, version = "1.0.0" }, paths = routes.GroupBy(route => route.Path).ToDictionary(group => group.Key, group => group.ToDictionary(route => route.Method, route => new { summary = route.Summary, responses = new Dictionary<string, object> { ["200"] = new { description = "Successful response" } } })) }); public const string Ui = """<!doctype html><html><head><title>MarketFlow API</title><link rel="stylesheet" href="https://unpkg.com/swagger-ui-dist@5/swagger-ui.css"></head><body><div id="swagger-ui"></div><script src="https://unpkg.com/swagger-ui-dist@5/swagger-ui-bundle.js"></script><script>SwaggerUIBundle({url:'openapi/v1.json',dom_id:'#swagger-ui'});</script></body></html>"""; }
