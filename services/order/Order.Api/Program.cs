using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using Confluent.Kafka;
using Microsoft.AspNetCore.Http.Features;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("Order") ?? "Host=localhost;Port=5432;Database=marketflow;Username=marketflow;Password=marketflow;Search Path=ordering"));
builder.Services.AddHttpClient<IdentityClient>(c => c.BaseAddress = new Uri(builder.Configuration["Services:IdentityUrl"] ?? "http://localhost:8081"));
builder.Services.AddHttpClient<CatalogClient>(c => c.BaseAddress = new Uri(builder.Configuration["Services:CatalogUrl"] ?? "http://localhost:8082"));
builder.Services.AddSingleton<RequestMetrics>();
builder.Services.AddHostedService<OutboxPublisher>();
builder.Services.AddHostedService<PendingOrderReconciler>();
builder.Services.AddSingleton(_ => new BankConfig(
    builder.Configuration["BankTransfer:BankName"] ?? "Bank of Ceylon",
    builder.Configuration["BankTransfer:AccountName"] ?? "TNT Supermarket",
    builder.Configuration["BankTransfer:AccountNumber"] ?? "0012345678",
    builder.Configuration["BankTransfer:Branch"] ?? "Colombo Main Branch"
));
builder.Services.AddSingleton(_ => new ReceiptStorageConfig(
    builder.Configuration["BankTransfer:ReceiptStoragePath"] ?? Path.Combine(AppContext.BaseDirectory, "receipts")
));
// Allow larger request bodies for PDF receipt upload (6 MB)
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 6 * 1024 * 1024);
var app = builder.Build();
app.Use(async (context, next) => { var correlation = context.Request.Headers["X-Correlation-Id"].FirstOrDefault() ?? Guid.NewGuid().ToString(); context.Response.Headers["X-Correlation-Id"] = correlation; var stopwatch = System.Diagnostics.Stopwatch.StartNew(); using (app.Logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = correlation })) { try { await next(); } finally { context.RequestServices.GetRequiredService<RequestMetrics>().Record(context.Response.StatusCode, stopwatch.Elapsed); } } });
if (app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("Migrations:ApplyOnStartup"))
    await OrderDb.InitializeAsync(app.Services.GetRequiredService<NpgsqlDataSource>());
if (builder.Configuration.GetValue<bool>("Migrations:ExitAfterApply")) return;
// Ensure receipt storage directory exists
var receiptStorage = app.Services.GetRequiredService<ReceiptStorageConfig>();
Directory.CreateDirectory(receiptStorage.BasePath);

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "order" }));
app.MapGet("/health/ready", async (NpgsqlDataSource db, IConfiguration cfg) => await DependencyHealth.Ready(db, cfg, "order"));
app.MapGet("/metrics", async (RequestMetrics m, NpgsqlDataSource db) => Results.Text(m.AsPrometheus("order", await OrderDb.OperationalMetrics(db)), "text/plain"));
app.MapGet("/openapi/v1.json", () => Results.Text(OpenApi.Document("MarketFlow Order API", new("get", "/health", "Liveness check"), new("get", "/health/ready", "Dependency readiness check"), new("get", "/addresses", "List customer addresses"), new("post", "/addresses", "Create an address"), new("put", "/addresses/{id}", "Update an address"), new("delete", "/addresses/{id}", "Delete an address"), new("get", "/basket", "Read the basket"), new("put", "/basket/items/{productId}", "Set basket item quantity"), new("post", "/orders", "Submit checkout"), new("get", "/orders", "List orders"), new("get", "/reports/sales", "Run the sales report"), new("get", "/reports/sales/export", "Export the sales report")), "application/json"));
app.MapGet("/swagger", () => Results.Content(OpenApi.Ui, "text/html"));
app.MapGet("/addresses", async (HttpRequest req, IdentityClient auth, NpgsqlDataSource db) => { var user = await auth.Customer(req); return user is null ? Results.Unauthorized() : Results.Ok(await OrderDb.Addresses(db, user.Subject)); });
app.MapGet("/addresses/{id:guid}", async (Guid id, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) => { var user = await auth.Customer(req); if (user is null) return Results.Unauthorized(); var address = await OrderDb.Address(db, user.Subject, id); return address is null ? Results.NotFound() : Results.Ok(address); });
app.MapPost("/addresses", async (AddressInput input, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) => { var user = await auth.Customer(req); if (user is null) return Results.Unauthorized(); var errors = AddressInput.Validate(input); if (errors.Count > 0) return Results.ValidationProblem(errors); var id = await OrderDb.SaveAddress(db, user.Subject, null, input); return Results.Created($"/addresses/{id}", new { id }); });
app.MapPut("/addresses/{id:guid}", async (Guid id, AddressInput input, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) => { var user = await auth.Customer(req); if (user is null) return Results.Unauthorized(); var errors = AddressInput.Validate(input); if (errors.Count > 0) return Results.ValidationProblem(errors); return await OrderDb.SaveAddress(db, user.Subject, id, input) == Guid.Empty ? Results.NotFound() : Results.NoContent(); });
app.MapDelete("/addresses/{id:guid}", async (Guid id, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) => { var user = await auth.Customer(req); if (user is null) return Results.Unauthorized(); return await OrderDb.DeleteAddress(db, user.Subject, id) ? Results.NoContent() : Results.NotFound(); });
app.MapGet("/basket", async (HttpRequest req, IdentityClient auth, NpgsqlDataSource db, CatalogClient catalog) => { var user = await auth.Customer(req); return user is null ? Results.Unauthorized() : Results.Ok(await OrderDb.Basket(db, user.Subject, catalog)); });
app.MapPut("/basket/items/{productId:guid}", async (Guid productId, BasketLineInput input, HttpRequest req, IdentityClient auth, NpgsqlDataSource db, CatalogClient catalog) => { var user = await auth.Customer(req); if (user is null) return Results.Unauthorized(); if (input.Quantity <= 0) return Results.ValidationProblem(new Dictionary<string, string[]> { ["quantity"] = ["Quantity must be positive."] }); var product = await catalog.Product(productId); if (product is null || !product.Active) return Results.BadRequest(new { message = "Product is not available." }); if (input.Quantity > product.StockQuantity) return Results.Conflict(new { message = $"Only {product.StockQuantity} items are available in stock." }); await OrderDb.SetBasketLine(db, user.Subject, productId, input.Quantity); return Results.Ok(await OrderDb.Basket(db, user.Subject, catalog)); });
app.MapDelete("/basket/items/{productId:guid}", async (Guid productId, HttpRequest req, IdentityClient auth, NpgsqlDataSource db, CatalogClient catalog) => { var user = await auth.Customer(req); if (user is null) return Results.Unauthorized(); await OrderDb.RemoveBasketLine(db, user.Subject, productId); return Results.Ok(await OrderDb.Basket(db, user.Subject, catalog)); });
app.MapPost("/orders", async (HttpRequest req, IdentityClient auth, NpgsqlDataSource db, CatalogClient catalog, ReceiptStorageConfig storageConfig) =>
{
    var user = await auth.Customer(req); if (user is null) return Results.Unauthorized();
    var key = req.Headers["Idempotency-Key"].FirstOrDefault(); if (string.IsNullOrWhiteSpace(key) || key.Length > 160) return Results.ValidationProblem(new Dictionary<string, string[]> { ["Idempotency-Key"] = ["A non-empty idempotency key of at most 160 characters is required."] });

    Guid addressId = Guid.Empty;
    string paymentMethod = "CashOnDelivery";
    IFormFile? receiptFile = null;

    if (req.HasFormContentType)
    {
        var form = await req.ReadFormAsync();
        if (!Guid.TryParse(form["addressId"].FirstOrDefault(), out addressId))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["addressId"] = ["A valid address ID is required."] });
        paymentMethod = form["paymentMethod"].FirstOrDefault() ?? "CashOnDelivery";
        receiptFile = form.Files.GetFile("receipt");
    }
    else
    {
        CheckoutInput? input = null;
        try
        {
            input = await JsonSerializer.DeserializeAsync<CheckoutInput>(req.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return Results.BadRequest(new { message = "Invalid JSON request body." });
        }

        if (input is null) return Results.BadRequest(new { message = "Invalid request body." });
        addressId = input.AddressId;
        paymentMethod = input.PaymentMethod ?? "CashOnDelivery";
    }

    if (paymentMethod != "CashOnDelivery" && paymentMethod != "BankTransfer")
        return Results.BadRequest(new { message = "Invalid payment method. Allowed: CashOnDelivery, BankTransfer." });

    // Validate receipt for BankTransfer
    string? receiptStorageKey = null; string? receiptOriginalName = null; string? receiptContentType = null; long receiptSize = 0;
    if (paymentMethod == "BankTransfer")
    {
        if (receiptFile is null || receiptFile.Length == 0)
            return Results.BadRequest(new { message = "A payment receipt PDF is required for Bank Transfer." });
        if (receiptFile.ContentType != "application/pdf" && !receiptFile.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(new { message = "Receipt must be a PDF file (application/pdf)." });
        if (receiptFile.Length > 5 * 1024 * 1024)
            return Results.BadRequest(new { message = "Receipt PDF must not exceed 5 MB." });

        // Additional MIME validation: check PDF magic bytes
        using var peek = new MemoryStream();
        await receiptFile.OpenReadStream().CopyToAsync(peek);
        peek.Position = 0;
        if (peek.Length < 4 || peek.ReadByte() != 0x25 || peek.ReadByte() != 0x50 || peek.ReadByte() != 0x44 || peek.ReadByte() != 0x46)
            return Results.BadRequest(new { message = "Uploaded file is not a valid PDF." });
        peek.Position = 0;

        receiptStorageKey = $"{Guid.NewGuid():N}.pdf";
        receiptOriginalName = receiptFile.FileName;
        receiptContentType = receiptFile.ContentType;
        receiptSize = receiptFile.Length;

        // Store receipt to disk
        var filePath = Path.Combine(storageConfig.BasePath, receiptStorageKey);
        try
        {
            await using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            await peek.CopyToAsync(fs);
        }
        catch
        {
            // Clean up on failure
            try { File.Delete(filePath); } catch { }
            return Results.Problem("Failed to store receipt file.", statusCode: 500);
        }
    }

    try
    {
        var result = await OrderDb.Checkout(db, user.Subject, key, new CheckoutInput(addressId, paymentMethod), catalog, CorrelationId.From(req));
        if (result.Error is "Conflict") return Results.Conflict(result);
        if (result.Status == "Rejected") return Results.UnprocessableEntity(result);

        // Create payment record
        var paymentStatus = paymentMethod == "BankTransfer" ? "PendingVerification" : "NotRequired";
        await OrderDb.CreatePayment(db, result.OrderId, paymentMethod, paymentStatus,
            receiptStorageKey, receiptOriginalName, receiptContentType, receiptSize);

        return Results.Ok(result);
    }
    catch
    {
        // Clean up stored receipt if order creation failed
        if (receiptStorageKey is not null)
        {
            try { File.Delete(Path.Combine(storageConfig.BasePath, receiptStorageKey)); } catch { }
        }
        throw;
    }
}).DisableAntiforgery();
app.MapGet("/orders", async (HttpRequest req, IdentityClient auth, NpgsqlDataSource db) => { var user = await auth.Customer(req); return user is null ? Results.Unauthorized() : Results.Ok(await OrderDb.Orders(db, user.Subject)); });
app.MapGet("/orders/my", async (HttpRequest req, IdentityClient auth, NpgsqlDataSource db) => { var user = await auth.Customer(req); return user is null ? Results.Unauthorized() : Results.Ok(await OrderDb.Orders(db, user.Subject)); });
app.MapGet("/orders/{id:guid}", async (Guid id, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) => { var user = await auth.Customer(req); if (user is null) return Results.Unauthorized(); var order = await OrderDb.Order(db, id, user.Subject, false); return order is null ? Results.NotFound() : Results.Ok(order); });
// Bank config endpoints (public)
app.MapGet("/bank-config", (BankConfig cfg) => Results.Ok(new { bankName = cfg.BankName, accountName = cfg.AccountName, accountNumber = cfg.AccountNumber, branch = cfg.Branch }));
app.MapGet("/orders/bank-config", (BankConfig cfg) => Results.Ok(new { bankName = cfg.BankName, accountName = cfg.AccountName, accountNumber = cfg.AccountNumber, branch = cfg.Branch }));
app.MapGet("/order/bank-config", (BankConfig cfg) => Results.Ok(new { bankName = cfg.BankName, accountName = cfg.AccountName, accountNumber = cfg.AccountNumber, branch = cfg.Branch }));
// Receipt access endpoint (protected)
app.MapGet("/orders/{id:guid}/payment/receipt", async (Guid id, HttpRequest req, IdentityClient auth, NpgsqlDataSource db, ReceiptStorageConfig storageConfig) =>
{
    // Allow customer (owner) or staff/admin
    var customer = await auth.Customer(req);
    var staff = customer is null ? await auth.Principal(req, "Staff", "Admin") : null;
    if (customer is null && staff is null) return Results.Unauthorized();
    var isStaff = staff is not null;
    var actorId = isStaff ? staff!.Subject : customer!.Subject;

    // Verify order exists and actor has access
    if (!isStaff) { var order = await OrderDb.Order(db, id, actorId, false); if (order is null) return Results.StatusCode(403); }

    var payment = await OrderDb.GetPayment(db, id);
    if (payment is null || payment.ReceiptStorageKey is null) return Results.NotFound(new { message = "No receipt found for this order." });

    var filePath = Path.Combine(storageConfig.BasePath, payment.ReceiptStorageKey);
    if (!File.Exists(filePath)) return Results.NotFound(new { message = "Receipt file not found." });

    return Results.File(filePath, payment.ReceiptContentType ?? "application/pdf", payment.ReceiptOriginalName ?? "receipt.pdf");
});
// Verify payment (staff/admin)
app.MapPost("/staff/orders/{id:guid}/payment/verify", async (Guid id, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) =>
{
    var actor = await auth.Principal(req, "Staff", "Admin"); if (actor is null) return Results.StatusCode(403);
    var payment = await OrderDb.GetPayment(db, id);
    if (payment is null) return Results.NotFound(new { message = "No payment record found for this order." });
    if (payment.PaymentStatus == "Verified") return Results.Conflict(new { message = "Payment is already verified." });
    if (payment.PaymentStatus == "Rejected") return Results.Conflict(new { message = "Payment was already rejected. Create a new order if needed." });
    if (payment.PaymentStatus != "PendingVerification") return Results.Conflict(new { message = "Payment cannot be verified in its current state." });
    await OrderDb.UpdatePaymentStatus(db, id, "Verified", actor.Subject, null);
    return Results.Ok(new { id, paymentStatus = "Verified" });
});
// Reject payment (staff/admin)
app.MapPost("/staff/orders/{id:guid}/payment/reject", async (Guid id, PaymentRejectInput input, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) =>
{
    var actor = await auth.Principal(req, "Staff", "Admin"); if (actor is null) return Results.StatusCode(403);
    if (string.IsNullOrWhiteSpace(input.Reason)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["Rejection reason is required."] });
    var payment = await OrderDb.GetPayment(db, id);
    if (payment is null) return Results.NotFound(new { message = "No payment record found for this order." });
    if (payment.PaymentStatus == "Verified") return Results.Conflict(new { message = "Payment is already verified." });
    if (payment.PaymentStatus == "Rejected") return Results.Conflict(new { message = "Payment is already rejected." });
    if (payment.PaymentStatus != "PendingVerification") return Results.Conflict(new { message = "Payment cannot be rejected in its current state." });
    await OrderDb.UpdatePaymentStatus(db, id, "Rejected", actor.Subject, input.Reason.Trim());
    return Results.Ok(new { id, paymentStatus = "Rejected" });
});
app.MapPost("/orders/{id:guid}/cancel", async (Guid id, CancelInput input, HttpRequest req, IdentityClient auth, NpgsqlDataSource db, CatalogClient catalog) => { var user = await auth.Customer(req); if (user is null) return Results.Unauthorized(); return await CancelOrder(db, catalog, id, user.Subject, input.Reason, false, CorrelationId.From(req)); });
app.MapGet("/staff/orders", async (HttpRequest req, IdentityClient auth, NpgsqlDataSource db) => !await auth.Allowed(req, "Staff", "Admin") ? Results.StatusCode(403) : Results.Ok(await OrderDb.StaffOrders(db)));
app.MapGet("/staff/orders/{id:guid}", async (Guid id, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) => { var actor = await auth.Principal(req, "Staff", "Admin"); if (actor is null) return Results.StatusCode(403); var order = await OrderDb.Order(db, id, actor.Subject, true); return order is null ? Results.NotFound() : Results.Ok(order); });
app.MapPost("/staff/orders/{id:guid}/cancel", async (Guid id, CancelInput input, HttpRequest req, IdentityClient auth, NpgsqlDataSource db, CatalogClient catalog) => { var actor = await auth.Principal(req, "Staff", "Admin"); if (actor is null) return Results.StatusCode(403); return await CancelOrder(db, catalog, id, actor.Subject, input.Reason, true, CorrelationId.From(req)); });
app.MapDelete("/staff/orders/{id:guid}", async (Guid id, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) => { var actor = await auth.Principal(req, "Staff", "Admin"); if (actor is null) return Results.StatusCode(403); return await OrderDb.DeleteTerminalOrder(db, id); });
app.MapPost("/staff/orders/{id:guid}/approve", async (Guid id, OrderActionInput input, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) => { var actor = await auth.Principal(req, "Staff", "Admin"); if (actor is null) return Results.StatusCode(403); var payment = await OrderDb.GetPayment(db, id); if (payment is not null && payment.PaymentMethod == "BankTransfer" && payment.PaymentStatus != "Verified") return Results.Conflict(new { message = "Bank Transfer orders can only be confirmed after payment is verified." }); return await OrderDb.Confirm(db, id, actor.Subject, input.Notes); });
app.MapPost("/staff/orders/{id:guid}/confirm", async (Guid id, OrderActionInput input, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) => { var actor = await auth.Principal(req, "Staff", "Admin"); if (actor is null) return Results.StatusCode(403); var payment = await OrderDb.GetPayment(db, id); if (payment is not null && payment.PaymentMethod == "BankTransfer" && payment.PaymentStatus != "Verified") return Results.Conflict(new { message = "Bank Transfer orders can only be confirmed after payment is verified." }); return await OrderDb.Confirm(db, id, actor.Subject, input.Notes); });
app.MapPost("/staff/orders/{id:guid}/reject", async (Guid id, OrderActionInput input, HttpRequest req, IdentityClient auth, NpgsqlDataSource db, CatalogClient catalog) => { var actor = await auth.Principal(req, "Staff", "Admin"); if (actor is null) return Results.StatusCode(403); if (string.IsNullOrWhiteSpace(input.Reason)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["Rejection reason is required."] }); return await RejectOrder(db, catalog, id, actor.Subject, input.Reason, CorrelationId.From(req)); });
app.MapGet("/staff/riders/available", async (string? zone, HttpRequest req, IdentityClient auth) => { var actor = await auth.Principal(req, "Staff", "Admin"); if (actor is null) return Results.StatusCode(403); return Results.Ok(await auth.AvailableRiders(req, zone)); });
app.MapPost("/staff/orders/{id:guid}/assign", async (Guid id, AssignRiderInput input, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) => { var actor = await auth.Principal(req, "Staff", "Admin"); if (actor is null) return Results.StatusCode(403); return await OrderDb.AssignRider(db, auth, req, id, input.RiderId, actor.Subject); });
app.MapPost("/staff/orders/{id:guid}/assign-rider", async (Guid id, AssignRiderInput input, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) => { var actor = await auth.Principal(req, "Staff", "Admin"); if (actor is null) return Results.StatusCode(403); return await OrderDb.AssignRider(db, auth, req, id, input.RiderId, actor.Subject); });
app.MapPost("/staff/orders/{id:guid}/start-delivery", async (Guid id, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) => { var actor = await auth.Principal(req, "Staff", "Admin"); if (actor is null) return Results.StatusCode(403); return await OrderDb.StartDelivery(db, auth, req, id, actor.Subject); });
app.MapPost("/staff/orders/{id:guid}/deliver", async (Guid id, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) => { var actor = await auth.Principal(req, "Staff", "Admin"); if (actor is null) return Results.StatusCode(403); return await OrderDb.Deliver(db, auth, req, id, actor.Subject); });
app.MapPost("/orders/{id:guid}/start-delivery", async (Guid id, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) => { var rider = await auth.Principal(req, "Rider"); if (rider is null) return Results.StatusCode(403); return await OrderDb.StartDelivery(db, auth, req, id, rider.Subject, rider.Subject); });
app.MapPost("/orders/{id:guid}/deliver", async (Guid id, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) => { var rider = await auth.Principal(req, "Rider"); if (rider is null) return Results.StatusCode(403); return await OrderDb.Deliver(db, auth, req, id, rider.Subject, rider.Subject); });
app.MapGet("/reports/sales", async (DateTimeOffset? from, DateTimeOffset? to, string? status, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) => { if (!await auth.Allowed(req, "Staff", "Admin")) return Results.StatusCode(403); if (from is not null && to is not null && from >= to) return Results.BadRequest(new { message = "from must precede to." }); return Results.Ok(await OrderDb.Sales(db, from, to, status)); });
app.MapGet("/reports/sales/export", async (DateTimeOffset? from, DateTimeOffset? to, string? status, HttpRequest req, IdentityClient auth, NpgsqlDataSource db) => { if (!await auth.Allowed(req, "Staff", "Admin")) return Results.StatusCode(403); if (from is not null && to is not null && from >= to) return Results.BadRequest(new { message = "from must precede to." }); var report = await OrderDb.Sales(db, from, to, status); var csv = new StringBuilder("Order ID,Created At,Status,Subtotal,Delivery Fee,Total,Currency\n"); foreach (var row in report.Orders) csv.Append(row.Id).Append(',').Append(row.CreatedAt.ToString("O")).Append(',').Append(row.Status).Append(',').Append(row.Subtotal.ToString("0.00", CultureInfo.InvariantCulture)).Append(',').Append(row.DeliveryFee.ToString("0.00", CultureInfo.InvariantCulture)).Append(',').Append(row.Total.ToString("0.00", CultureInfo.InvariantCulture)).Append(',').Append(row.Currency).Append('\n'); return Results.File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", "sales-report.csv"); });
app.Run();

static async Task<IResult> CancelOrder(NpgsqlDataSource db, CatalogClient catalog, Guid id, Guid actor, string reason, bool staff, Guid correlation)
{
    if (string.IsNullOrWhiteSpace(reason)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["Cancellation reason is required."] });
    var order = await OrderDb.Order(db, id, actor, staff); if (order is null) return Results.NotFound();
    if (order.Status != "Pending") return Results.Conflict(new { message = "This order can no longer be cancelled because it has already been confirmed or processed." });
    // The conditional update is the source of truth: a concurrent confirmation wins and prevents cancellation.
    if (!await OrderDb.Transition(db, id, "Pending", "Cancelled", actor, reason)) return Results.Conflict(new { message = "This order can no longer be cancelled because it has already been confirmed or processed." });
    _ = await catalog.Release(id, correlation);
    return Results.NoContent();
}
static async Task<IResult> RejectOrder(NpgsqlDataSource db, CatalogClient catalog, Guid id, Guid actor, string reason, Guid correlation)
{
    var order = await OrderDb.Order(db, id, actor, true); if (order is null) return Results.NotFound();
    if (order.Status != "Pending") return Results.Conflict(new { message = "Only pending orders can be rejected." });
    if (!await OrderDb.Transition(db, id, "Pending", "Rejected", actor, reason.Trim())) return Results.Conflict(new { message = "Order state changed; refresh and try again." });
    _ = await catalog.Release(id, correlation);
    return Results.NoContent();
}

record Principal(Guid Subject, string[] Roles);
record AddressInput(string RecipientName, string Phone, string Line1, string? Line2, string City, string Zone)
{
    public static Dictionary<string, string[]> Validate(AddressInput x) { var errors = new Dictionary<string, string[]>(); if (string.IsNullOrWhiteSpace(x.RecipientName) || string.IsNullOrWhiteSpace(x.Phone) || string.IsNullOrWhiteSpace(x.Line1) || string.IsNullOrWhiteSpace(x.City) || string.IsNullOrWhiteSpace(x.Zone)) errors["address"] = ["Recipient, phone, line 1, city and zone are required."]; return errors; }
}
record BasketLineInput(int Quantity); record CheckoutInput(Guid AddressId, string? PaymentMethod = null); record CancelInput(string Reason); record ReservationLine(Guid ProductId, int Quantity);
record OrderActionInput(string? Reason, string? Notes); record AssignRiderInput(Guid RiderId); record PaymentRejectInput(string Reason);
record BankConfig(string BankName, string AccountName, string AccountNumber, string Branch);
record ReceiptStorageConfig(string BasePath);
record PaymentRecord(Guid Id, Guid OrderId, string PaymentMethod, string PaymentStatus, string? ReceiptStorageKey, string? ReceiptOriginalName, string? ReceiptContentType, long ReceiptSize, DateTimeOffset? ReceiptUploadedAt, Guid? VerifiedBy, DateTimeOffset? VerifiedAt, Guid? RejectedBy, DateTimeOffset? RejectedAt, string? RejectionReason, DateTimeOffset CreatedAt);
record ProductDto(Guid Id, string Sku, string Name, decimal Price, int StockQuantity, bool Active);
record BasketLineView(Guid ProductId, string Sku, string Name, decimal UnitPrice, int Quantity, decimal LineTotal, bool Available, int StockQuantity);
record BasketView(List<BasketLineView> Lines, decimal Subtotal, decimal Tax, decimal DeliveryFee, decimal Total, string Currency);
record CheckoutResult(Guid OrderId, string Status, decimal Subtotal, decimal Tax, decimal DeliveryFee, decimal Total, string? Error = null);
record ReservationResponse(Guid OrderId, Guid? ReservationId, string State);
record SalesRow(Guid Id, DateTimeOffset CreatedAt, string Status, decimal Subtotal, decimal Tax, decimal DeliveryFee, decimal Total, string Currency);
record SalesReport(int OrderCount, decimal RecognizedSales, decimal Tax, decimal DeliveryFee, DateTimeOffset GeneratedAt, List<SalesRow> Orders);
static class CorrelationId { public static Guid From(HttpRequest request) => Guid.TryParse(request.Headers["X-Correlation-Id"].FirstOrDefault(), out var id) ? id : Guid.NewGuid(); }

sealed class IdentityClient(HttpClient http)
{
    public async Task<Principal?> Customer(HttpRequest request) => await Principal(request, "Customer");
    public async Task<bool> Allowed(HttpRequest request, params string[] roles) => await Principal(request, roles) is not null;
    public async Task<JsonElement[]> AvailableRiders(HttpRequest request, string? zone) { var token = request.Headers.Authorization.FirstOrDefault(); if (string.IsNullOrWhiteSpace(token)) return []; using var message = new HttpRequestMessage(HttpMethod.Get, $"/riders/available{(string.IsNullOrWhiteSpace(zone) ? "" : $"?zone={Uri.EscapeDataString(zone)}")}"); message.Headers.TryAddWithoutValidation("Authorization", token); try { var response = await http.SendAsync(message); if (!response.IsSuccessStatusCode) return []; return await response.Content.ReadFromJsonAsync<JsonElement[]>() ?? []; } catch { return []; } }
    public async Task<bool> SetAvailability(HttpRequest request, Guid riderId, string status) { var token = request.Headers.Authorization.FirstOrDefault(); if (string.IsNullOrWhiteSpace(token)) return false; using var message = new HttpRequestMessage(HttpMethod.Patch, $"/admin/users/{riderId}/availability") { Content = JsonContent.Create(new { availabilityStatus = status }) }; message.Headers.TryAddWithoutValidation("Authorization", token); try { return (await http.SendAsync(message)).IsSuccessStatusCode; } catch { return false; } }
    public async Task<Principal?> Principal(HttpRequest request, params string[] roles) { var token = request.Headers.Authorization.FirstOrDefault(); if (string.IsNullOrWhiteSpace(token)) return null; using var message = new HttpRequestMessage(HttpMethod.Get, "/auth/introspect"); message.Headers.TryAddWithoutValidation("Authorization", token); try { var result = await http.SendAsync(message); if (!result.IsSuccessStatusCode) return null; var root = JsonDocument.Parse(await result.Content.ReadAsStringAsync()).RootElement; if (!root.GetProperty("active").GetBoolean()) return null; var found = root.GetProperty("roles").EnumerateArray().Select(x => x.GetString()!).ToArray(); return roles.Any(found.Contains) ? new Principal(root.GetProperty("subject").GetGuid(), found) : null; } catch { return null; } }
}
sealed class CatalogClient(HttpClient http, IConfiguration config)
{
    string Key => config["Internal:CatalogKey"] ?? string.Empty;
    public async Task<ProductDto?> Product(Guid id) { try { var p = await http.GetFromJsonAsync<JsonElement>($"/products/{id}"); return p.ValueKind == JsonValueKind.Undefined ? null : new ProductDto(p.GetProperty("id").GetGuid(), p.GetProperty("sku").GetString()!, p.GetProperty("name").GetString()!, p.GetProperty("price").GetDecimal(), p.GetProperty("stockQuantity").GetInt32(), p.GetProperty("active").GetBoolean()); } catch { return null; } }
    public async Task<ReservationResponse?> Reserve(Guid orderId, List<ReservationLine> lines, Guid correlation) { if (string.IsNullOrWhiteSpace(Key)) return null; using var msg = new HttpRequestMessage(HttpMethod.Post, "/internal/stock-reservations") { Content = JsonContent.Create(new { orderId, lines, correlationId = correlation }) }; msg.Headers.Add("X-Internal-Key", Key); var result = await http.SendAsync(msg); return result.IsSuccessStatusCode ? await result.Content.ReadFromJsonAsync<ReservationResponse>() : null; }
    public async Task<ReservationResponse?> Release(Guid orderId, Guid correlation) { if (string.IsNullOrWhiteSpace(Key)) return null; using var msg = new HttpRequestMessage(HttpMethod.Post, $"/internal/stock-reservations/{orderId}/release") { Content = JsonContent.Create(new { correlationId = correlation }) }; msg.Headers.Add("X-Internal-Key", Key); var result = await http.SendAsync(msg); return result.IsSuccessStatusCode ? await result.Content.ReadFromJsonAsync<ReservationResponse>() : null; }
    public async Task<ReservationResponse?> Reservation(Guid orderId) { if (string.IsNullOrWhiteSpace(Key)) return null; using var msg = new HttpRequestMessage(HttpMethod.Get, $"/internal/stock-reservations/by-order/{orderId}"); msg.Headers.Add("X-Internal-Key", Key); var result = await http.SendAsync(msg); return result.IsSuccessStatusCode ? await result.Content.ReadFromJsonAsync<ReservationResponse>() : null; }
}

static class OrderDb
{
    public static async Task InitializeAsync(NpgsqlDataSource db)
    {
        await using (var history = db.CreateCommand("CREATE SCHEMA IF NOT EXISTS ordering; CREATE TABLE IF NOT EXISTS ordering.schema_migrations(version text PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now());")) await history.ExecuteNonQueryAsync();
        await using var conn = await db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using var claim = new NpgsqlCommand("INSERT INTO ordering.schema_migrations(version) VALUES('001_baseline') ON CONFLICT DO NOTHING RETURNING version", conn, tx);
        if (await claim.ExecuteScalarAsync() is not null)
        {
            var sql = await MigrationSql.BaselineAsync();
            await using var cmd = new NpgsqlCommand(sql, conn, tx); await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
        await ApplyMigration(db, "002_checkout_key_address", MigrationSql.CheckoutKeyAddressAsync);
        await ApplyMigration(db, "003_delivery_assignments", MigrationSql.DeliveryAssignmentsAsync);
        await ApplyMigration(db, "004_order_workflow", MigrationSql.OrderWorkflowAsync);
        await ApplyMigration(db, "005_order_payments", MigrationSql.OrderPaymentsAsync);
    }
    static async Task ApplyMigration(NpgsqlDataSource db, string version, Func<Task<string>> readSql)
    {
        await using var conn = await db.OpenConnectionAsync(); await using var tx = await conn.BeginTransactionAsync();
        await using var claim = new NpgsqlCommand("INSERT INTO ordering.schema_migrations(version) VALUES($1) ON CONFLICT DO NOTHING RETURNING version", conn, tx); claim.Parameters.AddWithValue(version);
        if (await claim.ExecuteScalarAsync() is not null) { await using var command = new NpgsqlCommand(await readSql(), conn, tx); await command.ExecuteNonQueryAsync(); }
        await tx.CommitAsync();
    }
    public static async Task<OperationalMetrics> OperationalMetrics(NpgsqlDataSource db) { await using var cmd = db.CreateCommand("SELECT (SELECT count(*) FROM ordering.outbox WHERE published_at IS NULL), (SELECT count(*) FROM ordering.orders WHERE status='Pending' AND reservation_id IS NULL)"); await using var reader = await cmd.ExecuteReaderAsync(); await reader.ReadAsync(); return new OperationalMetrics(reader.GetInt64(0), reader.GetInt64(1)); }
    public static async Task<List<object>> Addresses(NpgsqlDataSource db, Guid owner) { await using var cmd = db.CreateCommand("SELECT id,recipient_name,phone,line1,line2,city,zone FROM addresses WHERE customer_id=$1 ORDER BY updated_at DESC"); cmd.Parameters.AddWithValue(owner); await using var reader = await cmd.ExecuteReaderAsync(); var rows = new List<object>(); while (await reader.ReadAsync()) rows.Add(AddressRow(reader)); return rows; }
    public static async Task<object?> Address(NpgsqlDataSource db, Guid owner, Guid id) { await using var cmd = db.CreateCommand("SELECT id,recipient_name,phone,line1,line2,city,zone FROM addresses WHERE id=$1 AND customer_id=$2"); cmd.Parameters.AddWithValue(id); cmd.Parameters.AddWithValue(owner); await using var reader = await cmd.ExecuteReaderAsync(); return await reader.ReadAsync() ? AddressRow(reader) : null; }
    static object AddressRow(NpgsqlDataReader reader) => new { id = reader.GetGuid(0), recipientName = reader.GetString(1), phone = reader.GetString(2), line1 = reader.GetString(3), line2 = reader.IsDBNull(4) ? null : reader.GetString(4), city = reader.GetString(5), zone = reader.GetString(6) };
    public static async Task<Guid> SaveAddress(NpgsqlDataSource db, Guid owner, Guid? id, AddressInput input) { var value = id ?? Guid.NewGuid(); await using var cmd = db.CreateCommand(id is null ? "INSERT INTO addresses(id,customer_id,recipient_name,phone,line1,line2,city,zone) VALUES($1,$2,$3,$4,$5,$6,$7,$8)" : "UPDATE addresses SET recipient_name=$3,phone=$4,line1=$5,line2=$6,city=$7,zone=$8,updated_at=now() WHERE id=$1 AND customer_id=$2"); cmd.Parameters.AddWithValue(value); cmd.Parameters.AddWithValue(owner); cmd.Parameters.AddWithValue(input.RecipientName.Trim()); cmd.Parameters.AddWithValue(input.Phone.Trim()); cmd.Parameters.AddWithValue(input.Line1.Trim()); cmd.Parameters.AddWithValue((object?)input.Line2?.Trim() ?? DBNull.Value); cmd.Parameters.AddWithValue(input.City.Trim()); cmd.Parameters.AddWithValue(input.Zone.Trim()); return await cmd.ExecuteNonQueryAsync() == 0 ? Guid.Empty : value; }
    public static async Task<bool> DeleteAddress(NpgsqlDataSource db, Guid owner, Guid id) { await using var cmd = db.CreateCommand("DELETE FROM addresses WHERE id=$1 AND customer_id=$2"); cmd.Parameters.AddWithValue(id); cmd.Parameters.AddWithValue(owner); return await cmd.ExecuteNonQueryAsync() == 1; }
    public static async Task SetBasketLine(NpgsqlDataSource db, Guid owner, Guid product, int quantity) { await using var conn = await db.OpenConnectionAsync(); await using var tx = await conn.BeginTransactionAsync(); await using (var basket = new NpgsqlCommand("INSERT INTO baskets(customer_id) VALUES($1) ON CONFLICT(customer_id) DO UPDATE SET updated_at=now()", conn, tx)) { basket.Parameters.AddWithValue(owner); await basket.ExecuteNonQueryAsync(); } await using (var item = new NpgsqlCommand("INSERT INTO basket_items(customer_id,product_id,quantity) VALUES($1,$2,$3) ON CONFLICT(customer_id,product_id) DO UPDATE SET quantity=excluded.quantity", conn, tx)) { item.Parameters.AddWithValue(owner); item.Parameters.AddWithValue(product); item.Parameters.AddWithValue(quantity); await item.ExecuteNonQueryAsync(); } await tx.CommitAsync(); }
    public static async Task RemoveBasketLine(NpgsqlDataSource db, Guid owner, Guid product) { await using var cmd = db.CreateCommand("DELETE FROM basket_items WHERE customer_id=$1 AND product_id=$2"); cmd.Parameters.AddWithValue(owner); cmd.Parameters.AddWithValue(product); await cmd.ExecuteNonQueryAsync(); }
    public static async Task<BasketView> Basket(NpgsqlDataSource db, Guid owner, CatalogClient catalog) { await using var cmd = db.CreateCommand("SELECT product_id,quantity FROM basket_items WHERE customer_id=$1 ORDER BY product_id"); cmd.Parameters.AddWithValue(owner); await using var reader = await cmd.ExecuteReaderAsync(); var raw = new List<(Guid ProductId, int Quantity)>(); while (await reader.ReadAsync()) raw.Add((reader.GetGuid(0), reader.GetInt32(1))); var lines = new List<BasketLineView>(); decimal subtotal = 0; foreach (var item in raw) { var product = await catalog.Product(item.ProductId); if (product is null) { lines.Add(new BasketLineView(item.ProductId, "", "Unavailable product", 0, item.Quantity, 0, false, 0)); continue; } var total = product.Price * item.Quantity; subtotal += total; lines.Add(new BasketLineView(product.Id, product.Sku, product.Name, product.Price, item.Quantity, total, product.Active && product.StockQuantity >= item.Quantity, product.StockQuantity)); } var totals = CheckoutRules.Calculate(subtotal); return new BasketView(lines, totals.Subtotal, totals.Tax, totals.DeliveryFee, totals.Total, "Rs."); }
    public static async Task<CheckoutResult> Checkout(NpgsqlDataSource db, Guid owner, string key, CheckoutInput input, CatalogClient catalog, Guid correlation)
    {
        var address = await Address(db, owner, input.AddressId); if (address is null) return new CheckoutResult(Guid.Empty, "Rejected", 0, 0, 0, 0, "Choose one of your saved addresses.");
        var existing = await ExistingKey(db, owner, key); if (existing is not null) { if (existing.Value.AddressId != input.AddressId) return new CheckoutResult(existing.Value.OrderId, "Conflict", 0, 0, 0, 0, "Idempotency key was used with a different request."); return await ResultForOrder(db, existing.Value.OrderId); }
        var basket = await Basket(db, owner, catalog); if (basket.Lines.Count == 0 || basket.Lines.Any(x => !x.Available)) return new CheckoutResult(Guid.Empty, "Rejected", 0, 0, 0, 0, "Basket has unavailable items.");
        var hash = RequestHash(input.AddressId, basket);
        var orderId = Guid.NewGuid();
        try { await WritePending(db, orderId, owner, key, hash, new { Id = input.AddressId, Snapshot = address }, basket); }
        catch (PostgresException e) when (e.SqlState == "23505") { var duplicate = await ExistingKey(db, owner, key); if (duplicate is null) throw; return duplicate.Value.AddressId == input.AddressId ? await ResultForOrder(db, duplicate.Value.OrderId) : new CheckoutResult(duplicate.Value.OrderId, "Conflict", 0, 0, 0, 0, "Idempotency key was used with a different request."); }
        var reservation = await catalog.Reserve(orderId, basket.Lines.Select(x => new ReservationLine(x.ProductId, x.Quantity)).ToList(), correlation);
        if (reservation is null) return await ResultForOrder(db, orderId, "Reservation is pending reconciliation.");
        if (reservation.State == "Reserved") { await SetReservation(db, orderId, reservation.ReservationId); return await ResultForOrder(db, orderId); }
        await Finish(db, orderId, "Rejected", owner, "Stock unavailable", correlation, false); return await ResultForOrder(db, orderId, "Stock is no longer available.");
    }
    static string RequestHash(Guid addressId, BasketView basket) => CheckoutRules.IntentHash(addressId, basket.Lines.Select(x => new CheckoutLine(x.ProductId, x.Quantity, x.UnitPrice)), basket.Currency, basket.Tax, basket.DeliveryFee);
    static async Task<(string Hash, Guid OrderId, Guid AddressId)?> ExistingKey(NpgsqlDataSource db, Guid owner, string key) { await using var cmd = db.CreateCommand("SELECT request_hash,order_id,address_id FROM checkout_keys WHERE customer_id=$1 AND key=$2"); cmd.Parameters.AddWithValue(owner); cmd.Parameters.AddWithValue(key); await using var reader = await cmd.ExecuteReaderAsync(); return await reader.ReadAsync() ? (reader.GetString(0), reader.GetGuid(1), reader.GetGuid(2)) : null; }
    static async Task WritePending(NpgsqlDataSource db, Guid orderId, Guid owner, string key, string hash, object address, BasketView basket)
    {
        await using var conn = await db.OpenConnectionAsync(); await using var tx = await conn.BeginTransactionAsync();
        await using (var order = new NpgsqlCommand("INSERT INTO ordering.orders(id,customer_id,address_snapshot,status,subtotal,tax,delivery_fee,total,currency) VALUES($1,$2,CAST($3 AS jsonb),'Pending',$4,$5,$6,$7,$8)", conn, tx))
        { order.Parameters.AddWithValue(orderId); order.Parameters.AddWithValue(owner); order.Parameters.AddWithValue(JsonSerializer.Serialize(((dynamic)address).Snapshot)); order.Parameters.AddWithValue(basket.Subtotal); order.Parameters.AddWithValue(basket.Tax); order.Parameters.AddWithValue(basket.DeliveryFee); order.Parameters.AddWithValue(basket.Total); order.Parameters.AddWithValue(basket.Currency); await order.ExecuteNonQueryAsync(); }
        await using (var checkoutKey = new NpgsqlCommand("INSERT INTO ordering.checkout_keys(customer_id,key,request_hash,order_id,address_id) VALUES($1,$2,$3,$4,$5)", conn, tx))
        { checkoutKey.Parameters.AddWithValue(owner); checkoutKey.Parameters.AddWithValue(key); checkoutKey.Parameters.AddWithValue(hash); checkoutKey.Parameters.AddWithValue(orderId); checkoutKey.Parameters.AddWithValue(((dynamic)address).Id); await checkoutKey.ExecuteNonQueryAsync(); }
        await using (var history = new NpgsqlCommand("INSERT INTO ordering.order_status_history(order_id,status,actor_id,reason) VALUES($1,'Pending',$2,'Checkout received')", conn, tx))
        { history.Parameters.AddWithValue(orderId); history.Parameters.AddWithValue(owner); await history.ExecuteNonQueryAsync(); }
        foreach (var line in basket.Lines) { await using var item = new NpgsqlCommand("INSERT INTO ordering.order_items(order_id,product_id,sku,name,unit_price,quantity) VALUES($1,$2,$3,$4,$5,$6)", conn, tx); item.Parameters.AddWithValue(orderId); item.Parameters.AddWithValue(line.ProductId); item.Parameters.AddWithValue(line.Sku); item.Parameters.AddWithValue(line.Name); item.Parameters.AddWithValue(line.UnitPrice); item.Parameters.AddWithValue(line.Quantity); await item.ExecuteNonQueryAsync(); }
        await tx.CommitAsync();
    }
    public static async Task SetReservation(NpgsqlDataSource db, Guid orderId, Guid? reservationId)
    {
        await using var cmd = db.CreateCommand("UPDATE ordering.orders SET reservation_id=$2,updated_at=now() WHERE id=$1");
        cmd.Parameters.AddWithValue(orderId); cmd.Parameters.AddWithValue((object?)reservationId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }
    public static async Task Finish(NpgsqlDataSource db, Guid orderId, string state, Guid actor, string reason, Guid correlation, bool clearBasket) { await using var conn = await db.OpenConnectionAsync(); await using var tx = await conn.BeginTransactionAsync(); var eventType = state == "Confirmed" ? "OrderPlaced" : state == "Cancelled" ? "OrderCancelled" : "OrderRejected"; await using var read = new NpgsqlCommand("UPDATE ordering.orders SET status=$2,updated_at=now() WHERE id=$1 RETURNING customer_id,subtotal,tax,delivery_fee,total,currency", conn, tx); read.Parameters.AddWithValue(orderId); read.Parameters.AddWithValue(state); await using var reader = await read.ExecuteReaderAsync(); if (!await reader.ReadAsync()) return; var customer = reader.GetGuid(0); var subtotal = reader.GetDecimal(1); var tax = reader.GetDecimal(2); var fee = reader.GetDecimal(3); var total = reader.GetDecimal(4); var currency = reader.GetString(5); await reader.CloseAsync(); await using (var history = new NpgsqlCommand("INSERT INTO ordering.order_status_history(order_id,status,actor_id,reason) VALUES($1,$2,$3,$4)", conn, tx)) { history.Parameters.AddWithValue(orderId); history.Parameters.AddWithValue(state); history.Parameters.AddWithValue(actor); history.Parameters.AddWithValue(reason); await history.ExecuteNonQueryAsync(); } var eventId = Guid.NewGuid(); var payload = JsonSerializer.Serialize(new { eventId, schemaVersion = 1, eventType, occurredAt = DateTimeOffset.UtcNow, correlationId = correlation, orderId, customerId = customer, status = state, subtotal, tax, deliveryFee = fee, total, currency }); await using (var outbox = new NpgsqlCommand("INSERT INTO ordering.outbox(event_id,event_type,payload) VALUES($1,$2,CAST($3 AS jsonb))", conn, tx)) { outbox.Parameters.AddWithValue(eventId); outbox.Parameters.AddWithValue(eventType); outbox.Parameters.AddWithValue(payload); await outbox.ExecuteNonQueryAsync(); } if (clearBasket) { await using var clear = new NpgsqlCommand("DELETE FROM ordering.basket_items WHERE customer_id=$1", conn, tx); clear.Parameters.AddWithValue(customer); await clear.ExecuteNonQueryAsync(); } await tx.CommitAsync(); }
    public static async Task<List<object>> Orders(NpgsqlDataSource db, Guid owner) { await using var cmd = db.CreateCommand("SELECT o.id,o.status,o.total,o.created_at,o.address_snapshot::text,(SELECT count(*) FROM ordering.order_items i WHERE i.order_id=o.id),COALESCE(p.payment_method,'CashOnDelivery'),COALESCE(p.payment_status,'NotRequired'),p.rejection_reason FROM ordering.orders o LEFT JOIN ordering.order_payments p ON p.order_id=o.id WHERE o.customer_id=$1 ORDER BY o.created_at DESC"); cmd.Parameters.AddWithValue(owner); await using var reader = await cmd.ExecuteReaderAsync(); var rows = new List<object>(); while (await reader.ReadAsync()) { var address = JsonDocument.Parse(reader.GetString(4)).RootElement.Clone(); rows.Add(new { id = reader.GetGuid(0), status = reader.GetString(1), total = reader.GetDecimal(2), createdAt = reader.GetFieldValue<DateTimeOffset>(3), itemCount = reader.GetInt64(5), address, paymentMethod = reader.GetString(6), paymentStatus = reader.GetString(7), rejectionReason = reader.IsDBNull(8) ? null : reader.GetString(8) }); } return rows; }
    public static async Task<dynamic?> Order(NpgsqlDataSource db, Guid id, Guid owner, bool staff)
    {
        await using var cmd = db.CreateCommand(staff ? "SELECT o.id,o.customer_id,o.status,o.subtotal,o.tax,o.delivery_fee,o.total,o.currency,o.reservation_id,o.address_snapshot::text,o.created_at,o.updated_at,o.assigned_rider_id,o.assigned_at,COALESCE(p.payment_method,'CashOnDelivery'),COALESCE(p.payment_status,'NotRequired'),p.receipt_original_name,p.receipt_uploaded_at,p.rejection_reason FROM ordering.orders o LEFT JOIN ordering.order_payments p ON p.order_id=o.id WHERE o.id=$1" : "SELECT o.id,o.customer_id,o.status,o.subtotal,o.tax,o.delivery_fee,o.total,o.currency,o.reservation_id,o.address_snapshot::text,o.created_at,o.updated_at,o.assigned_rider_id,o.assigned_at,COALESCE(p.payment_method,'CashOnDelivery'),COALESCE(p.payment_status,'NotRequired'),p.receipt_original_name,p.receipt_uploaded_at,p.rejection_reason FROM ordering.orders o LEFT JOIN ordering.order_payments p ON p.order_id=o.id WHERE o.id=$1 AND o.customer_id=$2"); cmd.Parameters.AddWithValue(id); if (!staff) cmd.Parameters.AddWithValue(owner); await using var reader = await cmd.ExecuteReaderAsync(); if (!await reader.ReadAsync()) return null;
        var orderId = reader.GetGuid(0); var customerId = reader.GetGuid(1); var status = reader.GetString(2); var subtotal = reader.GetDecimal(3); var tax = reader.GetDecimal(4); var fee = reader.GetDecimal(5); var total = reader.GetDecimal(6); var currency = reader.GetString(7); var reservationId = reader.IsDBNull(8) ? (Guid?)null : reader.GetGuid(8); var address = JsonDocument.Parse(reader.GetString(9)).RootElement.Clone(); var createdAt = reader.GetFieldValue<DateTimeOffset>(10); var updatedAt = reader.GetFieldValue<DateTimeOffset>(11); var assignedRiderId = reader.IsDBNull(12) ? (Guid?)null : reader.GetGuid(12); var assignedAt = reader.IsDBNull(13) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(13);
        var paymentMethod = reader.GetString(14); var paymentStatus = reader.GetString(15); var receiptFileName = reader.IsDBNull(16) ? null : reader.GetString(16); var receiptUploadedAt = reader.IsDBNull(17) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(17); var rejectionReason = reader.IsDBNull(18) ? null : reader.GetString(18);
        await reader.CloseAsync();
        var items = new List<object>(); await using (var itemCmd = db.CreateCommand("SELECT product_id,sku,name,unit_price,quantity FROM ordering.order_items WHERE order_id=$1 ORDER BY sku")) { itemCmd.Parameters.AddWithValue(orderId); await using var itemReader = await itemCmd.ExecuteReaderAsync(); while (await itemReader.ReadAsync()) { var price = itemReader.GetDecimal(3); var quantity = itemReader.GetInt32(4); items.Add(new { productId = itemReader.GetGuid(0), sku = itemReader.GetString(1), name = itemReader.GetString(2), unitPrice = price, quantity, lineTotal = price * quantity }); } }
        var history = new List<object>(); await using (var historyCmd = db.CreateCommand("SELECT status,actor_id,reason,occurred_at FROM ordering.order_status_history WHERE order_id=$1 ORDER BY occurred_at")) { historyCmd.Parameters.AddWithValue(orderId); await using var historyReader = await historyCmd.ExecuteReaderAsync(); while (await historyReader.ReadAsync()) history.Add(new { status = historyReader.GetString(0), actorId = historyReader.IsDBNull(1) ? (Guid?)null : historyReader.GetGuid(1), reason = historyReader.IsDBNull(2) ? null : historyReader.GetString(2), changedAt = historyReader.GetFieldValue<DateTimeOffset>(3) }); }
        var reservationStatus = status switch { "Confirmed" => "Reserved", "Cancelled" => "Released", "Rejected" => "Failed", _ => status };
        return new { Id = orderId, CustomerId = customerId, Status = status, Subtotal = subtotal, Tax = tax, DeliveryFee = fee, Total = total, Currency = currency, ReservationId = reservationId, ReservationStatus = reservationStatus, AssignedRiderId = assignedRiderId, AssignedAt = assignedAt, Address = address, Items = items, StatusHistory = history, CreatedAt = createdAt, UpdatedAt = updatedAt, PaymentMethod = paymentMethod, PaymentStatus = paymentStatus, ReceiptFileName = receiptFileName, ReceiptUploadedAt = receiptUploadedAt, RejectionReason = rejectionReason };
    }
    public static async Task CreatePayment(NpgsqlDataSource db, Guid orderId, string paymentMethod, string paymentStatus, string? receiptStorageKey, string? receiptOriginalName, string? receiptContentType, long receiptSize)
    {
        await using var cmd = db.CreateCommand("INSERT INTO ordering.order_payments (order_id, payment_method, payment_status, receipt_storage_key, receipt_original_name, receipt_content_type, receipt_size, receipt_uploaded_at) VALUES ($1, $2, $3, $4, $5, $6, $7, CASE WHEN $4 IS NOT NULL THEN now() ELSE NULL END) ON CONFLICT (order_id) DO UPDATE SET payment_method=EXCLUDED.payment_method, payment_status=EXCLUDED.payment_status, receipt_storage_key=EXCLUDED.receipt_storage_key, receipt_original_name=EXCLUDED.receipt_original_name, receipt_content_type=EXCLUDED.receipt_content_type, receipt_size=EXCLUDED.receipt_size, receipt_uploaded_at=EXCLUDED.receipt_uploaded_at, updated_at=now()");
        cmd.Parameters.AddWithValue(orderId);
        cmd.Parameters.AddWithValue(paymentMethod);
        cmd.Parameters.AddWithValue(paymentStatus);
        cmd.Parameters.AddWithValue((object?)receiptStorageKey ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)receiptOriginalName ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)receiptContentType ?? DBNull.Value);
        cmd.Parameters.AddWithValue(receiptSize);
        await cmd.ExecuteNonQueryAsync();
    }
    public static async Task<PaymentRecord?> GetPayment(NpgsqlDataSource db, Guid orderId)
    {
        await using var cmd = db.CreateCommand("SELECT id, order_id, payment_method, payment_status, receipt_storage_key, receipt_original_name, receipt_content_type, COALESCE(receipt_size, 0), receipt_uploaded_at, verified_by, verified_at, rejected_by, rejected_at, rejection_reason, created_at FROM ordering.order_payments WHERE order_id=$1");
        cmd.Parameters.AddWithValue(orderId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new PaymentRecord(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetInt64(7),
            reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
            reader.IsDBNull(9) ? null : reader.GetGuid(9),
            reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
            reader.IsDBNull(11) ? null : reader.GetGuid(11),
            reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12),
            reader.IsDBNull(13) ? null : reader.GetString(13),
            reader.GetFieldValue<DateTimeOffset>(14)
        );
    }
    public static async Task<bool> UpdatePaymentStatus(NpgsqlDataSource db, Guid orderId, string status, Guid actorId, string? rejectionReason)
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        string sql = status switch
        {
            "Verified" => "UPDATE ordering.order_payments SET payment_status=$2, verified_by=$3, verified_at=now(), updated_at=now() WHERE order_id=$1",
            "Rejected" => "UPDATE ordering.order_payments SET payment_status=$2, rejected_by=$3, rejected_at=now(), rejection_reason=$4, updated_at=now() WHERE order_id=$1",
            _ => "UPDATE ordering.order_payments SET payment_status=$2, updated_at=now() WHERE order_id=$1"
        };
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue(orderId);
        cmd.Parameters.AddWithValue(status);
        cmd.Parameters.AddWithValue(actorId);
        if (status == "Rejected") cmd.Parameters.AddWithValue((object?)rejectionReason ?? DBNull.Value);
        var affected = await cmd.ExecuteNonQueryAsync();
        if (affected == 0) return false;
        await using var history = new NpgsqlCommand("INSERT INTO ordering.order_status_history(order_id,status,actor_id,reason) VALUES($1,$2,$3,$4)", conn, tx);
        history.Parameters.AddWithValue(orderId);
        history.Parameters.AddWithValue(status == "Verified" ? "PaymentVerified" : "PaymentRejected");
        history.Parameters.AddWithValue(actorId);
        history.Parameters.AddWithValue(rejectionReason ?? (status == "Verified" ? "Payment verified by staff" : "Payment rejected by staff"));
        await history.ExecuteNonQueryAsync();
        await tx.CommitAsync();
        return true;
    }
    public static async Task<List<object>> StaffOrders(NpgsqlDataSource db) { await using var cmd = db.CreateCommand("SELECT o.id,o.customer_id,o.status,o.total,o.created_at,o.reservation_id,(SELECT count(*) FROM ordering.order_items i WHERE i.order_id=o.id),o.assigned_rider_id,COALESCE(p.payment_method,'CashOnDelivery'),COALESCE(p.payment_status,'NotRequired'),p.receipt_original_name,p.receipt_uploaded_at,p.rejection_reason FROM ordering.orders o LEFT JOIN ordering.order_payments p ON p.order_id=o.id ORDER BY o.created_at DESC"); await using var reader = await cmd.ExecuteReaderAsync(); var rows = new List<object>(); while (await reader.ReadAsync()) { var currentStatus = reader.GetString(2); var reservationStatus = currentStatus switch { "Confirmed" => "Reserved", "Cancelled" => "Released", "Rejected" => "Failed", _ => currentStatus }; var assignedRiderId = reader.IsDBNull(7) ? (Guid?)null : reader.GetGuid(7); var deliveryStatus = currentStatus == "Delivered" ? "Delivered" : currentStatus == "Delivery" ? "In delivery" : assignedRiderId is not null ? "Rider assigned" : "Not assigned"; rows.Add(new { id = reader.GetGuid(0), customerId = reader.GetGuid(1), status = currentStatus, total = reader.GetDecimal(3), createdAt = reader.GetFieldValue<DateTimeOffset>(4), reservationId = reader.IsDBNull(5) ? (Guid?)null : reader.GetGuid(5), reservationStatus, itemCount = reader.GetInt64(6), assignedRiderId, deliveryStatus, paymentMethod = reader.GetString(8), paymentStatus = reader.GetString(9), receiptFileName = reader.IsDBNull(10) ? null : reader.GetString(10), receiptUploadedAt = reader.IsDBNull(11) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(11), rejectionReason = reader.IsDBNull(12) ? null : reader.GetString(12) }); } return rows; }
    public static async Task<IResult> DeleteTerminalOrder(NpgsqlDataSource db, Guid id) { await using var conn = await db.OpenConnectionAsync(); await using var tx = await conn.BeginTransactionAsync(); await using var status = new NpgsqlCommand("SELECT status FROM ordering.orders WHERE id=$1 FOR UPDATE", conn, tx); status.Parameters.AddWithValue(id); var value = await status.ExecuteScalarAsync(); if (value is null) return Results.NotFound(); if (value is not string currentStatus || (currentStatus != "Cancelled" && currentStatus != "Rejected")) return Results.Conflict(new { message = "Only cancelled or rejected orders can be deleted." }); foreach (var sql in new[] { "DELETE FROM ordering.order_status_history WHERE order_id=$1", "DELETE FROM ordering.order_items WHERE order_id=$1", "DELETE FROM ordering.checkout_keys WHERE order_id=$1", "DELETE FROM ordering.orders WHERE id=$1" }) { await using var cmd = new NpgsqlCommand(sql, conn, tx); cmd.Parameters.AddWithValue(id); await cmd.ExecuteNonQueryAsync(); } await tx.CommitAsync(); return Results.NoContent(); }
    public static async Task<IResult> Confirm(NpgsqlDataSource db, Guid id, Guid actor, string? notes) { if (!await Transition(db, id, "Pending", "Confirmed", actor, notes?.Trim() ?? "Confirmed by operations", "confirmed_by_user_id=$3,confirmed_at=now()")) return Results.Conflict(new { message = "Only pending orders can be confirmed." }); return Results.Ok(new { id, status = "Confirmed" }); }
    public static async Task<bool> Transition(NpgsqlDataSource db, Guid id, string from, string to, Guid actor, string reason, string? extraSet = null) { await using var conn = await db.OpenConnectionAsync(); await using var tx = await conn.BeginTransactionAsync(); await using var update = new NpgsqlCommand($"UPDATE ordering.orders SET status=$2,updated_at=now(){(extraSet is null ? "" : "," + extraSet)} WHERE id=$1 AND status=$4", conn, tx); update.Parameters.AddWithValue(id); update.Parameters.AddWithValue(to); update.Parameters.AddWithValue(actor); update.Parameters.AddWithValue(from); if (await update.ExecuteNonQueryAsync() != 1) return false; await using var history = new NpgsqlCommand("INSERT INTO ordering.order_status_history(order_id,status,actor_id,reason) VALUES($1,$2,$3,$4)", conn, tx); history.Parameters.AddWithValue(id); history.Parameters.AddWithValue(to); history.Parameters.AddWithValue(actor); history.Parameters.AddWithValue(reason); await history.ExecuteNonQueryAsync(); await tx.CommitAsync(); return true; }
    public static async Task<IResult> AssignRider(NpgsqlDataSource db, IdentityClient identity, HttpRequest request, Guid orderId, Guid riderId, Guid actor) { var order = await Order(db, orderId, actor, true); if (order is null) return Results.NotFound(); if (order.Status != "Confirmed") return Results.Conflict(new { message = "Only confirmed orders can be assigned a rider." }); var rider = (await identity.AvailableRiders(request, null)).FirstOrDefault(x => x.TryGetProperty("riderId", out var rid) && rid.GetGuid() == riderId); if (rider.ValueKind == JsonValueKind.Undefined) return Results.Conflict(new { message = "Rider is not active or available." }); await using var cmd = db.CreateCommand("UPDATE ordering.orders SET assigned_rider_id=$2,assigned_at=now(),assigned_by_user_id=$3,updated_at=now() WHERE id=$1 AND status='Confirmed'"); cmd.Parameters.AddWithValue(orderId); cmd.Parameters.AddWithValue(riderId); cmd.Parameters.AddWithValue(actor); if (await cmd.ExecuteNonQueryAsync() != 1) return Results.Conflict(new { message = "Order state changed; refresh and try again." }); return Results.Ok(new { orderId, riderId, status = "Confirmed" }); }
    public static async Task<IResult> StartDelivery(NpgsqlDataSource db, IdentityClient identity, HttpRequest request, Guid id, Guid actor, Guid? requiredRider = null) { await using var cmd = db.CreateCommand("SELECT assigned_rider_id FROM ordering.orders WHERE id=$1 AND status='Confirmed'"); cmd.Parameters.AddWithValue(id); var value = await cmd.ExecuteScalarAsync(); if (value is null) return Results.Conflict(new { message = "Only confirmed orders can start delivery." }); if (value is DBNull) return Results.UnprocessableEntity(new { message = "A rider must be assigned before starting delivery." }); var riderId = (Guid)value; if (requiredRider is not null && riderId != requiredRider) return Results.StatusCode(403); if (!await identity.SetAvailability(request, riderId, "Busy")) return Results.Conflict(new { message = "Rider is no longer available." }); if (!await Transition(db, id, "Confirmed", "Delivery", actor, "Delivery started")) { _ = await identity.SetAvailability(request, riderId, "Available"); return Results.Conflict(new { message = "Order state changed; refresh and try again." }); } return Results.Ok(new { id, status = "Delivery" }); }
    public static async Task<IResult> Deliver(NpgsqlDataSource db, IdentityClient identity, HttpRequest request, Guid id, Guid actor, Guid? requiredRider = null) { await using var cmd = db.CreateCommand("SELECT assigned_rider_id FROM ordering.orders WHERE id=$1 AND status='Delivery'"); cmd.Parameters.AddWithValue(id); var value = await cmd.ExecuteScalarAsync(); if (value is null || value is DBNull) return Results.Conflict(new { message = "Only orders in delivery can be marked delivered." }); var riderId = (Guid)value; if (requiredRider is not null && riderId != requiredRider) return Results.StatusCode(403); if (!await Transition(db, id, "Delivery", "Delivered", actor, "Delivery completed", "delivered_at=now()")) return Results.Conflict(new { message = "Order state changed; refresh and try again." }); _ = await identity.SetAvailability(request, riderId, "Available"); return Results.Ok(new { id, status = "Delivered" }); }
    static async Task<CheckoutResult> ResultForOrder(NpgsqlDataSource db, Guid orderId, string? error = null) { await using var cmd = db.CreateCommand("SELECT status,subtotal,tax,delivery_fee,total FROM ordering.orders WHERE id=$1"); cmd.Parameters.AddWithValue(orderId); await using var reader = await cmd.ExecuteReaderAsync(); return await reader.ReadAsync() ? new CheckoutResult(orderId, reader.GetString(0), reader.GetDecimal(1), reader.GetDecimal(2), reader.GetDecimal(3), reader.GetDecimal(4), error) : new CheckoutResult(orderId, "Pending", 0, 0, 0, 0, error); }
    public static async Task<List<Guid>> PendingOrders(NpgsqlDataSource db) { await using var cmd = db.CreateCommand("SELECT id FROM ordering.orders WHERE status='Pending' AND reservation_id IS NULL AND created_at < now() - interval '30 seconds' ORDER BY created_at LIMIT 20"); await using var reader = await cmd.ExecuteReaderAsync(); var ids = new List<Guid>(); while (await reader.ReadAsync()) ids.Add(reader.GetGuid(0)); return ids; }
    public static async Task<SalesReport> Sales(NpgsqlDataSource db, DateTimeOffset? from, DateTimeOffset? to, string? status) { await using var cmd = db.CreateCommand("SELECT id,created_at,status,subtotal,tax,delivery_fee,total,currency FROM ordering.orders WHERE ($1 IS NULL OR created_at >= $1) AND ($2 IS NULL OR created_at < $2) AND ($3 IS NULL OR status=$3) ORDER BY created_at DESC"); cmd.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.TimestampTz, (object?)from ?? DBNull.Value); cmd.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.TimestampTz, (object?)to ?? DBNull.Value); cmd.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Text, (object?)status?.Trim() ?? DBNull.Value); await using var reader = await cmd.ExecuteReaderAsync(); var rows = new List<SalesRow>(); while (await reader.ReadAsync()) rows.Add(new SalesRow(reader.GetGuid(0), reader.GetFieldValue<DateTimeOffset>(1), reader.GetString(2), reader.GetDecimal(3), reader.GetDecimal(4), reader.GetDecimal(5), reader.GetDecimal(6), reader.GetString(7))); var confirmed = rows.Where(x => x.Status == "Confirmed").ToList(); return new SalesReport(rows.Count, confirmed.Sum(x => x.Total), confirmed.Sum(x => x.Tax), confirmed.Sum(x => x.DeliveryFee), DateTimeOffset.UtcNow, rows); }
}
sealed class PendingOrderReconciler(IServiceProvider services, CatalogClient catalog, ILogger<PendingOrderReconciler> log) : BackgroundService { protected override async Task ExecuteAsync(CancellationToken stop) { while (!stop.IsCancellationRequested) { try { using var scope = services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>(); foreach (var id in await OrderDb.PendingOrders(db)) { var reservation = await catalog.Reservation(id); if (reservation?.State == "Reserved") await OrderDb.SetReservation(db, id, reservation.ReservationId); else if (reservation?.State == "Failed") await OrderDb.Finish(db, id, "Rejected", Guid.Empty, "Reservation failed", Guid.NewGuid(), false); } } catch (Exception ex) { log.LogWarning(ex, "Pending-order reconciliation will retry"); } await Task.Delay(TimeSpan.FromSeconds(15), stop); } } }
sealed class OutboxPublisher(IServiceProvider services, IConfiguration config, ILogger<OutboxPublisher> log) : BackgroundService { protected override async Task ExecuteAsync(CancellationToken stop) { using var producer = new ProducerBuilder<string, string>(KafkaClientSettings.Producer(config)).Build(); while (!stop.IsCancellationRequested) { try { using var scope = services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>(); await using var cmd = db.CreateCommand("SELECT event_id,payload::text FROM ordering.outbox WHERE published_at IS NULL ORDER BY occurred_at LIMIT 30"); await using var reader = await cmd.ExecuteReaderAsync(stop); var rows = new List<(Guid, string)>(); while (await reader.ReadAsync(stop)) rows.Add((reader.GetGuid(0), reader.GetString(1))); await reader.CloseAsync(); foreach (var row in rows) { await producer.ProduceAsync("order.events", new Message<string, string> { Key = row.Item1.ToString(), Value = row.Item2 }, stop); await using var done = db.CreateCommand("UPDATE ordering.outbox SET published_at=now() WHERE event_id=$1"); done.Parameters.AddWithValue(row.Item1); await done.ExecuteNonQueryAsync(stop); } } catch (Exception ex) { log.LogWarning(ex, "Order outbox publish will retry"); } await Task.Delay(TimeSpan.FromSeconds(3), stop); } } }
record OperationalMetrics(long OutboxPending, long PendingReservations);
static class MigrationSql { public static Task<string> BaselineAsync() => File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Migrations", "001_baseline.sql")); public static Task<string> CheckoutKeyAddressAsync() => File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Migrations", "002_checkout_key_address.sql")); public static Task<string> DeliveryAssignmentsAsync() => File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Migrations", "003_delivery_assignments.sql")); public static Task<string> OrderWorkflowAsync() => File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Migrations", "004_order_workflow.sql")); public static Task<string> OrderPaymentsAsync() => File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Migrations", "005_order_payments.sql")); }
static class DependencyHealth { public static async Task<IResult> Ready(NpgsqlDataSource db, IConfiguration cfg, string service) { try { await using var cmd = db.CreateCommand("SELECT 1"); await cmd.ExecuteScalarAsync(); using var kafka = new AdminClientBuilder(KafkaClientSettings.Admin(cfg)).Build(); var metadata = kafka.GetMetadata(TimeSpan.FromSeconds(2)); if (metadata.Brokers.Count == 0) throw new InvalidOperationException("Kafka has no available broker."); return Results.Ok(new { status = "ready", service, dependencies = new { postgres = "ready", kafka = "ready" } }); } catch { return Results.Problem("A required dependency is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable); } } }
sealed class RequestMetrics { long _requests; long _errors; long _durationTicks; public void Record(int status, TimeSpan duration) { Interlocked.Increment(ref _requests); if (status >= 500) Interlocked.Increment(ref _errors); Interlocked.Add(ref _durationTicks, duration.Ticks); } public string AsPrometheus(string service, OperationalMetrics? operational = null) => $"# TYPE marketflow_http_requests_total counter\nmarketflow_http_requests_total{{service=\"{service}\"}} {_requests}\n# TYPE marketflow_http_errors_total counter\nmarketflow_http_errors_total{{service=\"{service}\"}} {_errors}\n# TYPE marketflow_http_request_duration_seconds summary\nmarketflow_http_request_duration_seconds_sum{{service=\"{service}\"}} {TimeSpan.FromTicks(_durationTicks).TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}\nmarketflow_http_request_duration_seconds_count{{service=\"{service}\"}} {_requests}\n# TYPE marketflow_outbox_pending gauge\nmarketflow_outbox_pending{{service=\"{service}\"}} {operational?.OutboxPending ?? 0}\n# TYPE marketflow_pending_reservations gauge\nmarketflow_pending_reservations{{service=\"{service}\"}} {operational?.PendingReservations ?? 0}\n"; }
static class OpenApi { public record Route(string Method, string Path, string Summary); public static string Document(string title, params Route[] routes) => JsonSerializer.Serialize(new { openapi = "3.0.3", info = new { title, version = "1.0.0" }, paths = routes.GroupBy(route => route.Path).ToDictionary(group => group.Key, group => group.ToDictionary(route => route.Method, route => new { summary = route.Summary, responses = new Dictionary<string, object> { ["200"] = new { description = "Successful response" } } })) }); public const string Ui = """<!doctype html><html><head><title>MarketFlow API</title><link rel="stylesheet" href="https://unpkg.com/swagger-ui-dist@5/swagger-ui.css"></head><body><div id="swagger-ui"></div><script src="https://unpkg.com/swagger-ui-dist@5/swagger-ui-bundle.js"></script><script>SwaggerUIBundle({url:'openapi/v1.json',dom_id:'#swagger-ui'});</script></body></html>"""; }
