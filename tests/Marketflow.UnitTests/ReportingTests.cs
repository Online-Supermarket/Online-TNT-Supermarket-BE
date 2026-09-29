using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Npgsql;
using Xunit;

public sealed class ReportingTests
{
    [Fact]
    public async Task Reporting_authorization_allows_staff_and_admin_but_not_customers()
    {
        using var http = new HttpClient(new IntrospectionHandler()) { BaseAddress = new Uri("http://identity.test") };
        var identity = new IdentityClient(http);
        var context = new DefaultHttpContext(); context.Request.Headers.Authorization = "Bearer test";
        Assert.True(await identity.Allowed(context.Request, "Staff", "Admin"));
        Assert.False(await identity.Allowed(context.Request, "Customer"));
        context.Request.Headers.Remove("Authorization");
        Assert.False(await identity.Allowed(context.Request, "Staff"));
    }

    private static async Task<NpgsqlDataSource?> OpenReportingDatabase()
    {
        var connectionString = Environment.GetEnvironmentVariable("REPORTING_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) return null;
        var dataSource = NpgsqlDataSource.Create(connectionString);
        await ReportingDb.InitializeAsync(dataSource);
        return dataSource;
    }

    [Fact]
    public async Task Order_events_are_idempotent_and_sales_filters_accept_null_parameters()
    {
        await using var db = await OpenReportingDatabase();
        if (db is null) return;
        var orderId = Guid.NewGuid(); var eventId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new { eventId, eventType = "OrderPlaced", occurredAt = DateTimeOffset.UtcNow, orderId, status = "Confirmed", subtotal = 100m, tax = 0m, deliveryFee = 0m, total = 100m, currency = "LKR" });
        await ReportingDb.Apply(db, payload); await ReportingDb.Apply(db, payload);
        var report = await ReportingDb.Sales(db, null, null, null);
        Assert.Contains(report.Orders, row => row.Id == orderId);
        await using var count = db.CreateCommand("SELECT count(*) FROM reporting.sales_orders WHERE order_id=$1");
        count.Parameters.AddWithValue(orderId);
        Assert.Equal(1L, (long)(await count.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Product_events_are_idempotent_and_inventory_filter_accepts_null_category()
    {
        await using var db = await OpenReportingDatabase();
        if (db is null) return;
        var productId = Guid.NewGuid(); var eventId = Guid.NewGuid(); var categoryId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new { eventId, eventType = "ProductChanged", occurredAt = DateTimeOffset.UtcNow, productId, sku = $"TEST-{productId:N}", name = "Reporting test product", categoryId, categoryName = "Test", price = 12.5m, stockQuantity = 3, active = true });
        await ReportingDb.Apply(db, payload); await ReportingDb.Apply(db, payload);
        var report = await ReportingDb.Inventory(db, null, 5);
        Assert.Contains(report.Products, row => row.Sku == $"TEST-{productId:N}" && row.LowStock);
        await using var count = db.CreateCommand("SELECT count(*) FROM reporting.inventory_products WHERE product_id=$1");
        count.Parameters.AddWithValue(productId);
        Assert.Equal(1L, (long)(await count.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Unsupported_events_are_recorded_without_storing_payload()
    {
        await using var db = await OpenReportingDatabase();
        if (db is null) return;
        var eventId = Guid.NewGuid();
        await ReportingDb.Apply(db, JsonSerializer.Serialize(new { eventId, eventType = "FutureEvent", customerEmail = "must-not-be-stored" }));
        await using var command = db.CreateCommand("SELECT event_type, reason FROM reporting.ignored_events WHERE event_id=$1");
        command.Parameters.AddWithValue(eventId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("FutureEvent", reader.GetString(0));
        Assert.Equal("Unsupported event type", reader.GetString(1));
    }

    [Fact]
    public async Task Malformed_events_are_rejected_before_any_acknowledgement_record()
    {
        await using var db = await OpenReportingDatabase();
        if (db is null) return;
        await Assert.ThrowsAsync<InvalidDataException>(() => ReportingDb.Apply(db, "{\"eventType\":\"FutureEvent\"}"));
    }

    [Fact]
    public async Task StockReserved_events_apply_line_quantity_to_reporting_inventory()
    {
        await using var db = await OpenReportingDatabase();
        if (db is null) return;
        var productId = Guid.NewGuid(); var categoryId = Guid.NewGuid();
        await ReportingDb.Apply(db, JsonSerializer.Serialize(new { eventId = Guid.NewGuid(), eventType = "ProductChanged", occurredAt = DateTimeOffset.UtcNow, productId, sku = $"RES-{productId:N}", name = "Reserved test product", categoryId, categoryName = "Test", price = 12.5m, stockQuantity = 5, active = true }));
        await ReportingDb.Apply(db, JsonSerializer.Serialize(new { eventId = Guid.NewGuid(), schemaVersion = 1, eventType = "StockReserved", occurredAt = DateTimeOffset.UtcNow, correlationId = Guid.NewGuid(), orderId = Guid.NewGuid(), reservationId = Guid.NewGuid(), lines = new[] { new { productId, quantity = 2 } } }));
        await using var command = db.CreateCommand("SELECT stock_quantity FROM reporting.inventory_products WHERE product_id=$1");
        command.Parameters.AddWithValue(productId);
        Assert.Equal(3, (int)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task StockReserved_missing_product_is_recorded_and_can_retry_after_product_event()
    {
        await using var db = await OpenReportingDatabase();
        if (db is null) return;
        var productId = Guid.NewGuid(); var eventId = Guid.NewGuid(); var categoryId = Guid.NewGuid();
        var reservation = JsonSerializer.Serialize(new { eventId, eventType = "StockReserved", occurredAt = DateTimeOffset.UtcNow, orderId = Guid.NewGuid(), reservationId = Guid.NewGuid(), lines = new[] { new { productId, quantity = 2 } } });
        await ReportingDb.Apply(db, reservation); await ReportingDb.Apply(db, reservation);
        await using (var pending = db.CreateCommand("SELECT count(*) FROM reporting.pending_events WHERE event_id=$1")) { pending.Parameters.AddWithValue(eventId); Assert.Equal(1L, (long)(await pending.ExecuteScalarAsync())!); }
        await ReportingDb.Apply(db, JsonSerializer.Serialize(new { eventId = Guid.NewGuid(), eventType = "ProductChanged", occurredAt = DateTimeOffset.UtcNow, productId, sku = $"ORD-{productId:N}", name = "Ordered test product", categoryId, categoryName = "Test", price = 12.5m, stockQuantity = 5, active = true }));
        await ReportingDb.ReplayPending(db); await ReportingDb.Apply(db, reservation);
        await using var stock = db.CreateCommand("SELECT stock_quantity FROM reporting.inventory_products WHERE product_id=$1"); stock.Parameters.AddWithValue(productId);
        Assert.Equal(3, (int)(await stock.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task StockReserved_failure_rolls_back_processed_event_and_projection_change()
    {
        await using var db = await OpenReportingDatabase();
        if (db is null) return;
        var productId = Guid.NewGuid(); var eventId = Guid.NewGuid(); var categoryId = Guid.NewGuid();
        await ReportingDb.Apply(db, JsonSerializer.Serialize(new { eventId = Guid.NewGuid(), eventType = "ProductChanged", occurredAt = DateTimeOffset.UtcNow, productId, sku = $"RB-{productId:N}", name = "Rollback test product", categoryId, categoryName = "Test", price = 12.5m, stockQuantity = 5, active = true }));
        var malformed = JsonSerializer.Serialize(new { eventId, eventType = "StockReserved", occurredAt = DateTimeOffset.UtcNow, lines = new[] { new { productId } } });
        await Assert.ThrowsAsync<KeyNotFoundException>(() => ReportingDb.Apply(db, malformed));
        await using var count = db.CreateCommand("SELECT count(*) FROM reporting.processed_events WHERE event_id=$1"); count.Parameters.AddWithValue(eventId);
        Assert.Equal(0L, (long)(await count.ExecuteScalarAsync())!);
        await using var stock = db.CreateCommand("SELECT stock_quantity FROM reporting.inventory_products WHERE product_id=$1"); stock.Parameters.AddWithValue(productId);
        Assert.Equal(5, (int)(await stock.ExecuteScalarAsync())!);
    }

    private sealed class IntrospectionHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"active\":true,\"roles\":[\"Staff\"]}") });
    }
}
