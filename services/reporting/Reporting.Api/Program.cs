using System.Globalization;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Npgsql;
using NpgsqlTypes;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("Reporting") ?? "Host=localhost;Port=5432;Database=marketflow;Username=marketflow;Password=marketflow;Search Path=reporting"));
builder.Services.AddHttpClient<IdentityClient>(c => c.BaseAddress = new Uri(builder.Configuration["Services:IdentityUrl"] ?? "http://localhost:8081"));
builder.Services.AddSingleton<RequestMetrics>(); builder.Services.AddHostedService<EventConsumer>();
var app = builder.Build(); app.Use(async (ctx, next) => { ctx.RequestServices.GetRequiredService<RequestMetrics>().Increment(); await next(); }); if (app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("Migrations:ApplyOnStartup")) await ReportingDb.InitializeAsync(app.Services.GetRequiredService<NpgsqlDataSource>());
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "reporting" })); app.MapGet("/metrics", (RequestMetrics m) => Results.Text(m.AsPrometheus("reporting"), "text/plain"));
app.MapGet("/reports/inventory", async (Guid? categoryId, int? threshold, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) => !await auth.Allowed(req, "Staff", "Admin") ? Results.StatusCode(StatusCodes.Status403Forbidden) : Results.Ok(await ReportingDb.Inventory(db, categoryId, threshold ?? 5)));
app.MapGet("/reports/inventory/export", async (Guid? categoryId, int? threshold, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) => { if (!await auth.Allowed(req, "Staff", "Admin")) return Results.StatusCode(StatusCodes.Status403Forbidden); var report = await ReportingDb.Inventory(db, categoryId, threshold ?? 5); var csv = new StringBuilder("SKU,Name,Category,Price,Stock Quantity,Low Stock,Active\n"); foreach (var row in report.Products) csv.Append(Csv(row.Sku)).Append(',').Append(Csv(row.Name)).Append(',').Append(Csv(row.CategoryName)).Append(',').Append(row.Price.ToString("0.00", CultureInfo.InvariantCulture)).Append(',').Append(row.StockQuantity).Append(',').Append(row.LowStock).Append(',').Append(row.Active).Append('\n'); return Results.File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", "inventory-report.csv"); });
app.MapGet("/reports/sales", async (DateTimeOffset? from, DateTimeOffset? to, string? status, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) => { if (!await auth.Allowed(req, "Staff", "Admin")) return Results.StatusCode(StatusCodes.Status403Forbidden); if (from is not null && to is not null && from >= to) return Results.BadRequest(new { message = "from must precede to." }); return Results.Ok(await ReportingDb.Sales(db, from, to, status)); });
app.MapGet("/reports/sales/export", async (DateTimeOffset? from, DateTimeOffset? to, string? status, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) => { if (!await auth.Allowed(req, "Staff", "Admin")) return Results.StatusCode(StatusCodes.Status403Forbidden); if (from is not null && to is not null && from >= to) return Results.BadRequest(new { message = "from must precede to." }); var report = await ReportingDb.Sales(db, from, to, status); var csv = new StringBuilder("Order ID,Created At,Status,Subtotal,Tax,Delivery Fee,Total,Currency\n"); foreach (var row in report.Orders) csv.Append(row.Id).Append(',').Append(row.CreatedAt.ToString("O")).Append(',').Append(row.Status).Append(',').Append(row.Subtotal.ToString("0.00", CultureInfo.InvariantCulture)).Append(',').Append(row.Tax.ToString("0.00", CultureInfo.InvariantCulture)).Append(',').Append(row.DeliveryFee.ToString("0.00", CultureInfo.InvariantCulture)).Append(',').Append(row.Total.ToString("0.00", CultureInfo.InvariantCulture)).Append(',').Append(row.Currency).Append('\n'); return Results.File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", "sales-report.csv"); });
app.Run();

static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
public record Principal(Guid Subject, string[] Roles);
public record InventoryRow(string Sku, string Name, string CategoryName, decimal Price, int StockQuantity, bool LowStock, bool Active);
public record InventoryReport(int Threshold, int TotalProducts, int LowStockCount, int ZeroStockCount, DateTimeOffset LastUpdatedAt, IReadOnlyList<InventoryRow> Products);
public record SalesRow(Guid Id, DateTimeOffset CreatedAt, string Status, decimal Subtotal, decimal Tax, decimal DeliveryFee, decimal Total, string Currency);
public record SalesReport(DateTimeOffset? From, DateTimeOffset? To, string? Status, int OrderCount, decimal RecognizedSales, decimal Tax, decimal DeliveryFees, DateTimeOffset LastUpdatedAt, IReadOnlyList<SalesRow> Orders);
public sealed class IdentityClient(HttpClient http) { public async Task<bool> Allowed(HttpRequest req, params string[] roles) { var token = req.Headers.Authorization.FirstOrDefault(); if (token is null) return false; using var msg = new HttpRequestMessage(HttpMethod.Get, "/auth/introspect"); msg.Headers.TryAddWithoutValidation("Authorization", token); try { var res = await http.SendAsync(msg); if (!res.IsSuccessStatusCode) return false; var root = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement; return root.GetProperty("active").GetBoolean() && root.GetProperty("roles").EnumerateArray().Select(x => x.GetString()!).Any(roles.Contains); } catch { return false; } } }
public static class ReportingDb
{
    public static async Task InitializeAsync(NpgsqlDataSource db) { const string sql = """
    CREATE SCHEMA IF NOT EXISTS reporting;
    CREATE TABLE IF NOT EXISTS reporting.inventory_products(product_id uuid PRIMARY KEY,sku text NOT NULL,name text NOT NULL,category_id uuid NOT NULL,category_name text NOT NULL DEFAULT 'Unassigned',price numeric(12,2) NOT NULL,stock_quantity integer NOT NULL,active boolean NOT NULL,updated_at timestamptz NOT NULL);
    CREATE TABLE IF NOT EXISTS reporting.sales_orders(order_id uuid PRIMARY KEY,created_at timestamptz NOT NULL,status text NOT NULL,subtotal numeric(12,2) NOT NULL,tax numeric(12,2) NOT NULL,delivery_fee numeric(12,2) NOT NULL,total numeric(12,2) NOT NULL,currency text NOT NULL,updated_at timestamptz NOT NULL);
    CREATE TABLE IF NOT EXISTS reporting.processed_events(event_id uuid PRIMARY KEY,processed_at timestamptz NOT NULL DEFAULT now());
    """; await using var cmd = db.CreateCommand(sql); await cmd.ExecuteNonQueryAsync(); }
    public static async Task<InventoryReport> Inventory(NpgsqlDataSource db, Guid? categoryId, int threshold)
    {
        threshold = Math.Max(0, threshold);
        await using var cmd = db.CreateCommand("SELECT sku,name,category_name,price,stock_quantity,active,updated_at FROM inventory_products WHERE (@category_id IS NULL OR category_id=@category_id) ORDER BY name");
        Add(cmd, "category_id", NpgsqlDbType.Uuid, categoryId);
        await using var r = await cmd.ExecuteReaderAsync();
        var rows = new List<InventoryRow>(); var last = DateTimeOffset.MinValue;
        while (await r.ReadAsync())
        {
            var stock = r.GetInt32(4); var active = r.GetBoolean(5);
            rows.Add(new InventoryRow(r.GetString(0), r.GetString(1), r.GetString(2), r.GetDecimal(3), stock, active && stock <= threshold, active));
            var at = r.GetFieldValue<DateTimeOffset>(6); if (at > last) last = at;
        }
        var activeRows = rows.Where(x => x.Active).ToList();
        return new InventoryReport(threshold, activeRows.Count, activeRows.Count(x => x.LowStock), activeRows.Count(x => x.StockQuantity == 0), last, activeRows);
    }
    public static async Task<SalesReport> Sales(NpgsqlDataSource db, DateTimeOffset? from, DateTimeOffset? to, string? status)
    {
        await using var cmd = db.CreateCommand("SELECT order_id,created_at,status,subtotal,tax,delivery_fee,total,currency,updated_at FROM sales_orders WHERE (@from_date IS NULL OR created_at >= @from_date) AND (@to_date IS NULL OR created_at < @to_date) AND (@status_filter IS NULL OR status=@status_filter) ORDER BY created_at DESC");
        Add(cmd, "from_date", NpgsqlDbType.TimestampTz, from); Add(cmd, "to_date", NpgsqlDbType.TimestampTz, to); Add(cmd, "status_filter", NpgsqlDbType.Text, string.IsNullOrWhiteSpace(status) ? null : status.Trim());
        await using var r = await cmd.ExecuteReaderAsync();
        var rows = new List<SalesRow>(); var last = DateTimeOffset.MinValue;
        while (await r.ReadAsync())
        {
            rows.Add(new SalesRow(r.GetGuid(0), r.GetFieldValue<DateTimeOffset>(1), r.GetString(2), r.GetDecimal(3), r.GetDecimal(4), r.GetDecimal(5), r.GetDecimal(6), r.GetString(7)));
            var at = r.GetFieldValue<DateTimeOffset>(8); if (at > last) last = at;
        }
        var recognized = rows.Where(x => x.Status == "Confirmed").ToList();
        return new SalesReport(from, to, status, rows.Count, recognized.Sum(x => x.Total), recognized.Sum(x => x.Tax), recognized.Sum(x => x.DeliveryFee), last, rows);
    }
    public static async Task Apply(NpgsqlDataSource db, string json)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        if (!root.TryGetProperty("eventId", out var eventId) || !root.TryGetProperty("eventType", out var eventType)) return;
        var type = eventType.GetString();
        if (type is not ("ProductChanged" or "OrderPlaced" or "OrderRejected" or "OrderCancelled" or "StockAdjusted" or "StockReplenished" or "StockReserved" or "StockReleased")) return;
        var id = eventId.GetGuid();
        await using var conn = await db.OpenConnectionAsync();
        await using var transaction = await conn.BeginTransactionAsync();
        await using (var seen = new NpgsqlCommand("INSERT INTO reporting.processed_events(event_id) VALUES(@event_id) ON CONFLICT DO NOTHING", conn, transaction))
        {
            Add(seen, "event_id", NpgsqlDbType.Uuid, id);
            if (await seen.ExecuteNonQueryAsync() == 0) { await transaction.CommitAsync(); return; }
        }
        if (type == "ProductChanged")
        {
            await using var upsert = new NpgsqlCommand("INSERT INTO reporting.inventory_products(product_id,sku,name,category_id,category_name,price,stock_quantity,active,updated_at) VALUES(@product_id,@sku,@name,@category_id,@category_name,@price,@stock_quantity,@active,@updated_at) ON CONFLICT(product_id) DO UPDATE SET sku=excluded.sku,name=excluded.name,category_id=excluded.category_id,category_name=excluded.category_name,price=excluded.price,stock_quantity=excluded.stock_quantity,active=excluded.active,updated_at=excluded.updated_at", conn, transaction);
            Add(upsert, "product_id", NpgsqlDbType.Uuid, root.GetProperty("productId").GetGuid());
            Add(upsert, "sku", NpgsqlDbType.Text, root.GetProperty("sku").GetString());
            Add(upsert, "name", NpgsqlDbType.Text, root.GetProperty("name").GetString());
            Add(upsert, "category_id", NpgsqlDbType.Uuid, root.GetProperty("categoryId").GetGuid());
            Add(upsert, "category_name", NpgsqlDbType.Text, root.TryGetProperty("categoryName", out var categoryName) ? categoryName.GetString() : "Unassigned");
            Add(upsert, "price", NpgsqlDbType.Numeric, root.GetProperty("price").GetDecimal());
            Add(upsert, "stock_quantity", NpgsqlDbType.Integer, root.GetProperty("stockQuantity").GetInt32());
            Add(upsert, "active", NpgsqlDbType.Boolean, root.GetProperty("active").GetBoolean());
            Add(upsert, "updated_at", NpgsqlDbType.TimestampTz, root.GetProperty("occurredAt").GetDateTimeOffset());
            await upsert.ExecuteNonQueryAsync();
        }
        else if (type is "OrderPlaced" or "OrderRejected")
        {
            await using var upsert = new NpgsqlCommand("INSERT INTO reporting.sales_orders(order_id,created_at,status,subtotal,tax,delivery_fee,total,currency,updated_at) VALUES(@order_id,@occurred_at,@status,@subtotal,@tax,@delivery_fee,@total,@currency,@occurred_at) ON CONFLICT(order_id) DO UPDATE SET status=excluded.status,subtotal=excluded.subtotal,tax=excluded.tax,delivery_fee=excluded.delivery_fee,total=excluded.total,currency=excluded.currency,updated_at=excluded.updated_at", conn, transaction);
            AddOrderEvent(upsert, root); await upsert.ExecuteNonQueryAsync();
        }
        else if (type == "OrderCancelled")
        {
            await using var cancel = new NpgsqlCommand("UPDATE reporting.sales_orders SET status=@status,updated_at=@occurred_at WHERE order_id=@order_id", conn, transaction);
            Add(cancel, "order_id", NpgsqlDbType.Uuid, root.GetProperty("orderId").GetGuid());
            Add(cancel, "status", NpgsqlDbType.Text, "Cancelled");
            Add(cancel, "occurred_at", NpgsqlDbType.TimestampTz, root.GetProperty("occurredAt").GetDateTimeOffset());
            await cancel.ExecuteNonQueryAsync();
        }
        else if (type == "StockAdjusted")
        {
            await using var update = new NpgsqlCommand("UPDATE reporting.inventory_products SET stock_quantity=@stock_quantity,updated_at=@updated_at WHERE product_id=@product_id", conn, transaction);
            Add(update, "product_id", NpgsqlDbType.Uuid, root.GetProperty("productId").GetGuid());
            Add(update, "stock_quantity", NpgsqlDbType.Integer, root.GetProperty("newQuantity").GetInt32());
            Add(update, "updated_at", NpgsqlDbType.TimestampTz, root.GetProperty("occurredAt").GetDateTimeOffset());
            await update.ExecuteNonQueryAsync();
        }
        else if (type == "StockReplenished")
        {
            await ApplyStockDelta(conn, transaction, root.GetProperty("productId").GetGuid(), root.GetProperty("receivedQuantity").GetInt32(), root.GetProperty("occurredAt").GetDateTimeOffset());
        }
        else
        {
            var delta = type == "StockReserved" ? -1 : 1;
            foreach (var line in root.GetProperty("lines").EnumerateArray())
                await ApplyStockDelta(conn, transaction, line.GetProperty("productId").GetGuid(), delta * line.GetProperty("quantity").GetInt32(), root.GetProperty("occurredAt").GetDateTimeOffset());
        }
        await transaction.CommitAsync();
    }
    static async Task ApplyStockDelta(NpgsqlConnection conn, NpgsqlTransaction transaction, Guid productId, int delta, DateTimeOffset occurredAt)
    {
        await using var update = new NpgsqlCommand("UPDATE reporting.inventory_products SET stock_quantity=stock_quantity+@delta,updated_at=@updated_at WHERE product_id=@product_id", conn, transaction);
        Add(update, "product_id", NpgsqlDbType.Uuid, productId); Add(update, "delta", NpgsqlDbType.Integer, delta); Add(update, "updated_at", NpgsqlDbType.TimestampTz, occurredAt); await update.ExecuteNonQueryAsync();
    }
    static void AddOrderEvent(NpgsqlCommand c, JsonElement root)
    {
        Add(c, "order_id", NpgsqlDbType.Uuid, root.GetProperty("orderId").GetGuid());
        Add(c, "occurred_at", NpgsqlDbType.TimestampTz, root.GetProperty("occurredAt").GetDateTimeOffset());
        Add(c, "status", NpgsqlDbType.Text, root.GetProperty("status").GetString());
        Add(c, "subtotal", NpgsqlDbType.Numeric, root.GetProperty("subtotal").GetDecimal());
        Add(c, "tax", NpgsqlDbType.Numeric, root.GetProperty("tax").GetDecimal());
        Add(c, "delivery_fee", NpgsqlDbType.Numeric, root.GetProperty("deliveryFee").GetDecimal());
        Add(c, "total", NpgsqlDbType.Numeric, root.GetProperty("total").GetDecimal());
        Add(c, "currency", NpgsqlDbType.Text, root.GetProperty("currency").GetString());
    }
    static NpgsqlParameter Add(NpgsqlCommand command, string name, NpgsqlDbType type, object? value)
    {
        var parameter = command.Parameters.Add(name, type); parameter.Value = value ?? DBNull.Value; return parameter;
    }
}
sealed class EventConsumer(IServiceProvider services, IConfiguration cfg, ILogger<EventConsumer> log) : BackgroundService { protected override async Task ExecuteAsync(CancellationToken stop) { var config = new ConsumerConfig { BootstrapServers = cfg["Kafka:BootstrapServers"] ?? "localhost:9092", GroupId = "reporting-v2", AutoOffsetReset = AutoOffsetReset.Earliest, EnableAutoCommit = false }; while (!stop.IsCancellationRequested) { try { using var consumer = new ConsumerBuilder<string, string>(config).Build(); consumer.Subscribe(["catalog.events", "order.events"]); while (!stop.IsCancellationRequested) { var msg = consumer.Consume(stop); using var scope = services.CreateScope(); await ReportingDb.Apply(scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>(), msg.Message.Value); consumer.Commit(msg); } } catch (OperationCanceledException) { break; } catch (Exception ex) { log.LogWarning(ex, "Reporting consumer will retry"); await Task.Delay(TimeSpan.FromSeconds(3), stop); } } } }
sealed class RequestMetrics { long _requests; public void Increment() => Interlocked.Increment(ref _requests); public string AsPrometheus(string s) => $"# TYPE marketflow_http_requests_total counter\nmarketflow_http_requests_total{{service=\"{s}\"}} {_requests}\n"; }
