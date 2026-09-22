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
await CatalogDb.InitializeAsync(app.Services.GetRequiredService<NpgsqlDataSource>());

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "catalog" }));
app.MapGet("/health/ready", async (NpgsqlDataSource db, IConfiguration cfg) => await DependencyHealth.Ready(db, cfg, "catalog"));
app.MapGet("/metrics", async (RequestMetrics m, NpgsqlDataSource db) => Results.Text(m.AsPrometheus("catalog", await CatalogDb.OperationalMetrics(db)), "text/plain"));
app.MapGet("/openapi/v1.json", () => Results.Text(OpenApi.Document("MarketFlow Catalog API", new("get", "/health", "Liveness check"), new("get", "/health/ready", "Dependency readiness check"), new("get", "/categories", "List categories"), new("get", "/products", "Browse products"), new("post", "/products", "Create a product"), new("put", "/products/{id}", "Update a product"), new("delete", "/products/{id}", "Deactivate a product"), new("get", "/reports/inventory", "Run the inventory report"), new("get", "/reports/inventory/export", "Export the inventory report")), "application/json"));
app.MapGet("/swagger", () => Results.Content(OpenApi.Ui, "text/html"));
app.MapGet("/categories", CatalogDb.Categories);
app.MapGet("/products", CatalogDb.Products);
app.MapGet("/products/{id:guid}", CatalogDb.Product);
app.MapGet("/staff/products", async (HttpRequest req, IdentityClient auth, NpgsqlDataSource db) => !await auth.Allowed(req, "CatalogStaff", "OperationsAdmin") ? Results.Forbid() : Results.Ok(await CatalogDb.ReadProducts(db, true)));
app.MapPost("/products", async (ProductInput input, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) =>
{
    var actor = await auth.Principal(req, "CatalogStaff", "OperationsAdmin"); if (actor is null) return Results.Forbid();
    var errors = ProductInput.Validate(input); if (errors.Count > 0) return Results.ValidationProblem(errors);
    try { var id = await CatalogDb.UpsertProduct(db, null, input, actor.Subject, CorrelationId.From(req)); return Results.Created($"/products/{id}", new { id }); }
    catch (PostgresException e) when (e.SqlState == "23505") { return Results.Conflict(new { message = "SKU already exists." }); }
});
app.MapPut("/products/{id:guid}", async (Guid id, ProductInput input, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) =>
{
    var actor = await auth.Principal(req, "CatalogStaff", "OperationsAdmin"); if (actor is null) return Results.Forbid();
    var errors = ProductInput.Validate(input); if (errors.Count > 0) return Results.ValidationProblem(errors);
    try { return await CatalogDb.UpsertProduct(db, id, input, actor.Subject, CorrelationId.From(req)) == Guid.Empty ? Results.NotFound() : Results.NoContent(); }
    catch (PostgresException e) when (e.SqlState == "23505") { return Results.Conflict(new { message = "SKU already exists." }); }
});
app.MapMethods("/products/{id:guid}/deactivate", ["PATCH"], async (Guid id, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) =>
{
    var actor = await auth.Principal(req, "CatalogStaff", "OperationsAdmin"); if (actor is null) return Results.Forbid();
    return await CatalogDb.Deactivate(db, id, actor.Subject, CorrelationId.From(req)) ? Results.NoContent() : Results.NotFound();
});
app.MapGet("/reports/inventory", async (Guid? categoryId, int? threshold, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) =>
    !await auth.Allowed(req, "InventoryStaff", "CatalogStaff", "OperationsAdmin") ? Results.Forbid() : Results.Ok(await CatalogDb.Inventory(db, categoryId, threshold ?? 5)));
app.MapGet("/reports/inventory/export", async (Guid? categoryId, int? threshold, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) =>
{
    if (!await auth.Allowed(req, "InventoryStaff", "CatalogStaff", "OperationsAdmin")) return Results.Forbid();
    var report = await CatalogDb.Inventory(db, categoryId, threshold ?? 5); var csv = new StringBuilder("SKU,Name,Category,Price,Stock Quantity,Low Stock,Active\n");
    foreach (var row in report.Products) csv.Append(Csv.Escape(row.Sku)).Append(',').Append(Csv.Escape(row.Name)).Append(',').Append(Csv.Escape(row.CategoryName)).Append(',').Append(row.Price.ToString("0.00", CultureInfo.InvariantCulture)).Append(',').Append(row.StockQuantity).Append(',').Append(row.LowStock).Append(',').Append(row.Active).Append('\n');
    return Results.File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", "inventory-report.csv");
});
app.MapPost("/internal/stock-reservations", async (ReservationRequest request, HttpRequest req, IConfiguration cfg, NpgsqlDataSource db) =>
{
    if (!InternalAuth.Valid(req, cfg)) return Results.Unauthorized();
    if (request.OrderId == Guid.Empty || request.Lines.Count == 0 || request.Lines.Any(x => x.ProductId == Guid.Empty || x.Quantity <= 0)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["lines"] = ["An order ID and positive product quantities are required."] });
    return Results.Ok(await CatalogDb.Reserve(db, request));
});
app.MapGet("/internal/stock-reservations/by-order/{orderId:guid}", async (Guid orderId, HttpRequest req, IConfiguration cfg, NpgsqlDataSource db) => !InternalAuth.Valid(req, cfg) ? Results.Unauthorized() : Results.Ok(await CatalogDb.Reservation(db, orderId)));
app.MapPost("/internal/stock-reservations/{orderId:guid}/release", async (Guid orderId, ReleaseRequest request, HttpRequest req, IConfiguration cfg, NpgsqlDataSource db) => !InternalAuth.Valid(req, cfg) ? Results.Unauthorized() : Results.Ok(await CatalogDb.Release(db, orderId, request.CorrelationId == Guid.Empty ? CorrelationId.From(req) : request.CorrelationId)));
app.Run();

record ProductInput(string Sku, string Name, string? Description, decimal Price, int StockQuantity, Guid CategoryId)
{
    public static Dictionary<string, string[]> Validate(ProductInput p) { var e = new Dictionary<string, string[]>(); if (string.IsNullOrWhiteSpace(p.Sku)) e["sku"] = ["SKU is required."]; if (string.IsNullOrWhiteSpace(p.Name)) e["name"] = ["Name is required."]; if (p.Price < 0) e["price"] = ["Price cannot be negative."]; if (p.StockQuantity < 0) e["stockQuantity"] = ["Stock cannot be negative."]; if (p.CategoryId == Guid.Empty) e["categoryId"] = ["Category is required."]; return e; }
}
record Principal(Guid Subject, string[] Roles);
record ReservationLine(Guid ProductId, int Quantity);
record ReservationRequest(Guid OrderId, List<ReservationLine> Lines, Guid CorrelationId);
record ReleaseRequest(Guid CorrelationId);
record ReservationResult(Guid OrderId, Guid? ReservationId, string State, List<object> Lines);
record InventoryRow(string Sku, string Name, string CategoryName, decimal Price, int StockQuantity, bool LowStock, bool Active);
record InventoryReport(int TotalProducts, int LowStockCount, int ZeroStockCount, DateTimeOffset GeneratedAt, List<InventoryRow> Products);
record ProductSnapshot(Guid Id, string Sku, string Name, decimal Price, int StockQuantity, bool Active, Guid CategoryId);

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
    public static async Task InitializeAsync(NpgsqlDataSource db)
    {
        await using (var history = db.CreateCommand("CREATE SCHEMA IF NOT EXISTS catalog; CREATE TABLE IF NOT EXISTS catalog.schema_migrations(version text PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now());")) await history.ExecuteNonQueryAsync();
        await using var conn = await db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using var claim = new NpgsqlCommand("INSERT INTO catalog.schema_migrations(version) VALUES('001_baseline') ON CONFLICT DO NOTHING RETURNING version", conn, tx);
        if (await claim.ExecuteScalarAsync() is null) { await tx.CommitAsync(); return; }
        var sql = await MigrationSql.BaselineAsync();
        await using var cmd = new NpgsqlCommand(sql, conn, tx); await cmd.ExecuteNonQueryAsync();
        foreach (var category in new[] { ("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "Fruit & Vegetables"), ("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", "Pantry") }) { await using var seed = new NpgsqlCommand("INSERT INTO catalog.categories(id,name) VALUES($1,$2) ON CONFLICT DO NOTHING", conn, tx); seed.Parameters.AddWithValue(Guid.Parse(category.Item1)); seed.Parameters.AddWithValue(category.Item2); await seed.ExecuteNonQueryAsync(); }
        await tx.CommitAsync();
    }
    public static async Task<OperationalMetrics> OperationalMetrics(NpgsqlDataSource db) { await using var cmd = db.CreateCommand("SELECT (SELECT count(*) FROM catalog.outbox WHERE published_at IS NULL), (SELECT count(*) FROM catalog.stock_reservations WHERE state='Failed')"); await using var reader = await cmd.ExecuteReaderAsync(); await reader.ReadAsync(); return new OperationalMetrics(reader.GetInt64(0), reader.GetInt64(1)); }
    public static async Task<IResult> Categories(NpgsqlDataSource db) { await using var cmd = db.CreateCommand("SELECT id,name FROM categories ORDER BY name"); await using var reader = await cmd.ExecuteReaderAsync(); var rows = new List<object>(); while (await reader.ReadAsync()) rows.Add(new { id = reader.GetGuid(0), name = reader.GetString(1) }); return Results.Ok(rows); }
    public static async Task<IResult> Products(string? q, Guid? categoryId, int page, int pageSize, NpgsqlDataSource db)
    {
        page = Math.Max(1, page == 0 ? 1 : page);
        pageSize = Math.Clamp(pageSize == 0 ? 20 : pageSize, 1, 100);
        await using var cmd = db.CreateCommand("SELECT p.id,p.sku,p.name,p.description,p.price,p.stock_quantity,p.active,c.id,c.name FROM products p JOIN categories c ON c.id=p.category_id WHERE p.active=true AND ($1 IS NULL OR p.name ILIKE '%'||$1||'%' OR p.sku ILIKE '%'||$1||'%') AND ($2 IS NULL OR p.category_id=$2) ORDER BY p.name OFFSET $3 LIMIT $4");
        cmd.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)q?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue(NpgsqlDbType.Uuid, (object?)categoryId ?? DBNull.Value);
        cmd.Parameters.AddWithValue((page - 1) * pageSize);
        cmd.Parameters.AddWithValue(pageSize);
        return Results.Ok(await ReadProducts(cmd));
    }

    public static async Task<IResult> Product(Guid id, NpgsqlDataSource db) { await using var cmd = db.CreateCommand("SELECT p.id,p.sku,p.name,p.description,p.price,p.stock_quantity,p.active,c.id,c.name FROM products p JOIN categories c ON c.id=p.category_id WHERE p.id=$1 AND p.active=true"); cmd.Parameters.AddWithValue(id); var item = (await ReadProducts(cmd)).SingleOrDefault(); return item is null ? Results.NotFound() : Results.Ok(item); }
    public static async Task<List<object>> ReadProducts(NpgsqlDataSource db, bool all) { await using var cmd = db.CreateCommand($"SELECT p.id,p.sku,p.name,p.description,p.price,p.stock_quantity,p.active,c.id,c.name FROM products p JOIN categories c ON c.id=p.category_id {(all ? "" : "WHERE p.active=true")} ORDER BY p.name"); return await ReadProducts(cmd); }
    static async Task<List<object>> ReadProducts(NpgsqlCommand cmd) { await using var reader = await cmd.ExecuteReaderAsync(); var rows = new List<object>(); while (await reader.ReadAsync()) rows.Add(new { id = reader.GetGuid(0), sku = reader.GetString(1), name = reader.GetString(2), description = reader.IsDBNull(3) ? null : reader.GetString(3), price = reader.GetDecimal(4), stockQuantity = reader.GetInt32(5), available = reader.GetInt32(5) > 0, active = reader.GetBoolean(6), category = new { id = reader.GetGuid(7), name = reader.GetString(8) } }); return rows; }
    public static async Task<Guid> UpsertProduct(NpgsqlDataSource db, Guid? id, ProductInput input, Guid actor, Guid correlation)
    {
        await using var conn = await db.OpenConnectionAsync(); await using var tx = await conn.BeginTransactionAsync(); var value = id ?? Guid.NewGuid(); int? priorStock = null;
        if (id is not null) { await using var prior = new NpgsqlCommand("SELECT stock_quantity FROM catalog.products WHERE id=$1 FOR UPDATE", conn, tx); prior.Parameters.AddWithValue(value); var priorValue = await prior.ExecuteScalarAsync(); if (priorValue is null) return Guid.Empty; priorStock = Convert.ToInt32(priorValue, CultureInfo.InvariantCulture); }
        await using var write = new NpgsqlCommand(id is null ? "INSERT INTO catalog.products(id,sku,name,description,price,stock_quantity,category_id,created_by,updated_by) VALUES($1,$2,$3,$4,$5,$6,$7,$8,$8)" : "UPDATE catalog.products SET sku=$2,name=$3,description=$4,price=$5,stock_quantity=$6,category_id=$7,updated_by=$8,updated_at=now() WHERE id=$1", conn, tx);
        write.Parameters.AddWithValue(value); write.Parameters.AddWithValue(input.Sku.Trim()); write.Parameters.AddWithValue(input.Name.Trim()); write.Parameters.AddWithValue((object?)input.Description?.Trim() ?? DBNull.Value); write.Parameters.AddWithValue(input.Price); write.Parameters.AddWithValue(input.StockQuantity); write.Parameters.AddWithValue(input.CategoryId); write.Parameters.AddWithValue(actor); await write.ExecuteNonQueryAsync();
        var snapshot = await Snapshot(conn, tx, value); await AuditAndOutbox(conn, tx, snapshot!, "ProductChanged", actor, correlation);
        if (priorStock is not null && priorStock != input.StockQuantity) await StockMovement(conn, tx, value, input.StockQuantity - priorStock.Value, "ManualAdjustment", actor, correlation);
        await tx.CommitAsync(); return value;
    }
    public static async Task<bool> Deactivate(NpgsqlDataSource db, Guid id, Guid actor, Guid correlation)
    {
        await using var conn = await db.OpenConnectionAsync(); await using var tx = await conn.BeginTransactionAsync(); await using var write = new NpgsqlCommand("UPDATE catalog.products SET active=false,updated_by=$2,updated_at=now() WHERE id=$1 AND active=true", conn, tx); write.Parameters.AddWithValue(id); write.Parameters.AddWithValue(actor); if (await write.ExecuteNonQueryAsync() == 0) return false;
        await AuditAndOutbox(conn, tx, (await Snapshot(conn, tx, id))!, "ProductChanged", actor, correlation); await tx.CommitAsync(); return true;
    }
    static async Task<ProductSnapshot?> Snapshot(NpgsqlConnection conn, NpgsqlTransaction tx, Guid id) { await using var cmd = new NpgsqlCommand("SELECT id,sku,name,price,stock_quantity,active,category_id FROM catalog.products WHERE id=$1", conn, tx); cmd.Parameters.AddWithValue(id); await using var reader = await cmd.ExecuteReaderAsync(); return await reader.ReadAsync() ? new ProductSnapshot(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetDecimal(3), reader.GetInt32(4), reader.GetBoolean(5), reader.GetGuid(6)) : null; }
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
            categoryId = product.CategoryId
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
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = config["Kafka:BootstrapServers"] ?? "localhost:9092", MessageTimeoutMs = 3000 }).Build();
        while (!stop.IsCancellationRequested) { try { using var scope = services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>(); await using var cmd = db.CreateCommand("SELECT event_id,payload::text FROM catalog.outbox WHERE published_at IS NULL ORDER BY occurred_at LIMIT 30"); await using var reader = await cmd.ExecuteReaderAsync(stop); var rows = new List<(Guid, string)>(); while (await reader.ReadAsync(stop)) rows.Add((reader.GetGuid(0), reader.GetString(1))); await reader.CloseAsync(); foreach (var row in rows) { await producer.ProduceAsync("catalog.events", new Message<string, string> { Key = row.Item1.ToString(), Value = row.Item2 }, stop); await using var done = db.CreateCommand("UPDATE catalog.outbox SET published_at=now() WHERE event_id=$1"); done.Parameters.AddWithValue(row.Item1); await done.ExecuteNonQueryAsync(stop); } } catch (Exception ex) { log.LogWarning(ex, "Catalog outbox publish will retry"); } await Task.Delay(TimeSpan.FromSeconds(3), stop); }
    }
}
record OperationalMetrics(long OutboxPending, long FailedReservations);
static class MigrationSql { public static Task<string> BaselineAsync() => File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Migrations", "001_baseline.sql")); }
static class DependencyHealth { public static async Task<IResult> Ready(NpgsqlDataSource db, IConfiguration cfg, string service) { try { await using var cmd = db.CreateCommand("SELECT 1"); await cmd.ExecuteScalarAsync(); using var kafka = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = cfg["Kafka:BootstrapServers"] ?? "localhost:9092", SocketTimeoutMs = 2000 }).Build(); var metadata = kafka.GetMetadata(TimeSpan.FromSeconds(2)); if (metadata.Brokers.Count == 0) throw new InvalidOperationException("Kafka has no available broker."); return Results.Ok(new { status = "ready", service, dependencies = new { postgres = "ready", kafka = "ready" } }); } catch { return Results.Problem("A required dependency is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable); } } }
sealed class RequestMetrics { long _requests; long _errors; long _durationTicks; public void Record(int status, TimeSpan duration) { Interlocked.Increment(ref _requests); if (status >= 500) Interlocked.Increment(ref _errors); Interlocked.Add(ref _durationTicks, duration.Ticks); } public string AsPrometheus(string service, OperationalMetrics? operational = null) => $"# TYPE marketflow_http_requests_total counter\nmarketflow_http_requests_total{{service=\"{service}\"}} {_requests}\n# TYPE marketflow_http_errors_total counter\nmarketflow_http_errors_total{{service=\"{service}\"}} {_errors}\n# TYPE marketflow_http_request_duration_seconds summary\nmarketflow_http_request_duration_seconds_sum{{service=\"{service}\"}} {TimeSpan.FromTicks(_durationTicks).TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}\nmarketflow_http_request_duration_seconds_count{{service=\"{service}\"}} {_requests}\n# TYPE marketflow_outbox_pending gauge\nmarketflow_outbox_pending{{service=\"{service}\"}} {operational?.OutboxPending ?? 0}\n# TYPE marketflow_failed_reservations_total counter\nmarketflow_failed_reservations_total{{service=\"{service}\"}} {operational?.FailedReservations ?? 0}\n"; }
static class OpenApi { public record Route(string Method, string Path, string Summary); public static string Document(string title, params Route[] routes) => JsonSerializer.Serialize(new { openapi = "3.0.3", info = new { title, version = "1.0.0" }, paths = routes.GroupBy(route => route.Path).ToDictionary(group => group.Key, group => group.ToDictionary(route => route.Method, route => new { summary = route.Summary, responses = new Dictionary<string, object> { ["200"] = new { description = "Successful response" } } })) }); public const string Ui = """<!doctype html><html><head><title>MarketFlow API</title><link rel="stylesheet" href="https://unpkg.com/swagger-ui-dist@5/swagger-ui.css"></head><body><div id="swagger-ui"></div><script src="https://unpkg.com/swagger-ui-dist@5/swagger-ui-bundle.js"></script><script>SwaggerUIBundle({url:'openapi/v1.json',dom_id:'#swagger-ui'});</script></body></html>"""; }
