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

    private sealed class IntrospectionHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"active\":true,\"roles\":[\"Staff\"]}") });
    }
}
