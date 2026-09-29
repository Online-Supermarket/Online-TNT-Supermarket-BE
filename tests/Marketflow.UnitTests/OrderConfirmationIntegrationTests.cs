using Microsoft.AspNetCore.Http;
using Npgsql;
using Order.Api;
using Xunit;

public sealed class OrderConfirmationIntegrationTests
{
    private const string TestConnectionVariable = "ORDER_TEST_CONNECTION";

    [Fact]
    public async Task Confirm_requires_a_payment_record()
    {
        await using var db = await OpenTestDatabase();
        if (db is null) return;
        var orderId = await InsertPendingOrder(db, paymentMethod: null, paymentStatus: null);

        var result = await OrderDb.Confirm(db, orderId, Guid.NewGuid(), null);

        Assert.Equal(StatusCodes.Status409Conflict, Status(result));
        Assert.Equal("Pending", await OrderStatus(db, orderId));
    }

    [Fact]
    public async Task Confirm_allows_cod_once_but_rejects_repeated_confirmation()
    {
        await using var db = await OpenTestDatabase();
        if (db is null) return;
        var orderId = await InsertPendingOrder(db, PaymentRules.CashOnDelivery, "NotRequired");

        Assert.Equal(StatusCodes.Status200OK, Status(await OrderDb.Confirm(db, orderId, Guid.NewGuid(), "test")));
        Assert.Equal(StatusCodes.Status409Conflict, Status(await OrderDb.Confirm(db, orderId, Guid.NewGuid(), "repeat")));
        Assert.Equal("Confirmed", await OrderStatus(db, orderId));
    }

    [Theory]
    [InlineData("PendingVerification", StatusCodes.Status409Conflict)]
    [InlineData("Rejected", StatusCodes.Status409Conflict)]
    [InlineData("Verified", StatusCodes.Status200OK)]
    public async Task Confirm_requires_verified_bank_transfer(string paymentStatus, int expectedStatus)
    {
        await using var db = await OpenTestDatabase();
        if (db is null) return;
        var orderId = await InsertPendingOrder(db, PaymentRules.BankTransfer, paymentStatus);

        Assert.Equal(expectedStatus, Status(await OrderDb.Confirm(db, orderId, Guid.NewGuid(), null)));
        Assert.Equal(expectedStatus == StatusCodes.Status200OK ? "Confirmed" : "Pending", await OrderStatus(db, orderId));
    }

    [Fact]
    public async Task Payment_mutation_and_confirmation_serialize_on_the_pending_order()
    {
        await using var db = await OpenTestDatabase();
        if (db is null) return;
        var orderId = await InsertPendingOrder(db, PaymentRules.BankTransfer, "PendingVerification");

        var confirm = OrderDb.Confirm(db, orderId, Guid.NewGuid(), null);
        var reject = OrderDb.UpdatePaymentStatus(db, orderId, "Rejected", Guid.NewGuid(), "test rejection");
        await Task.WhenAll(confirm, reject);

        var orderStatus = await OrderStatus(db, orderId);
        var paymentStatus = await PaymentStatus(db, orderId);
        Assert.True(
            (orderStatus == "Confirmed" && paymentStatus == "Verified") ||
            (orderStatus == "Pending" && paymentStatus == "Rejected"),
            $"Invalid final state: order={orderStatus}, payment={paymentStatus}");
    }

    [Fact]
    public async Task Verification_and_confirmation_leave_a_verified_payment_and_never_confirm_an_unverified_one()
    {
        await using var db = await OpenTestDatabase();
        if (db is null) return;
        var orderId = await InsertPendingOrder(db, PaymentRules.BankTransfer, "PendingVerification");

        var confirm = OrderDb.Confirm(db, orderId, Guid.NewGuid(), null);
        var verify = OrderDb.UpdatePaymentStatus(db, orderId, "Verified", Guid.NewGuid(), null);
        await Task.WhenAll(confirm, verify);

        var orderStatus = await OrderStatus(db, orderId);
        Assert.Equal("Verified", await PaymentStatus(db, orderId));
        Assert.Contains(orderStatus, new[] { "Pending", "Confirmed" });
    }

    [Fact]
    public async Task Cancellation_and_confirmation_cannot_both_transition_a_cod_order()
    {
        await using var db = await OpenTestDatabase();
        if (db is null) return;
        var orderId = await InsertPendingOrder(db, PaymentRules.CashOnDelivery, "NotRequired");

        var confirm = OrderDb.Confirm(db, orderId, Guid.NewGuid(), null);
        var cancel = OrderDb.Transition(db, orderId, "Pending", "Cancelled", Guid.NewGuid(), "concurrency test");
        await Task.WhenAll(confirm, cancel);

        var status = await OrderStatus(db, orderId);
        Assert.Contains(status, new[] { "Confirmed", "Cancelled" });
    }

    private static async Task<NpgsqlDataSource?> OpenTestDatabase()
    {
        var connectionString = Environment.GetEnvironmentVariable(TestConnectionVariable);
        if (string.IsNullOrWhiteSpace(connectionString)) return null;
        var db = NpgsqlDataSource.Create(connectionString);
        await OrderDb.InitializeAsync(db);
        return db;
    }

    private static async Task<Guid> InsertPendingOrder(NpgsqlDataSource db, string? paymentMethod, string? paymentStatus)
    {
        var orderId = Guid.NewGuid();
        await using (var order = db.CreateCommand("INSERT INTO ordering.orders(id,customer_id,address_snapshot,status,subtotal,tax,delivery_fee,total,currency) VALUES($1,$2,$3::jsonb,'Pending',1,0,0,1,'LKR')"))
        {
            order.Parameters.AddWithValue(orderId);
            order.Parameters.AddWithValue(Guid.NewGuid());
            order.Parameters.AddWithValue("{}");
            await order.ExecuteNonQueryAsync();
        }
        if (paymentMethod is not null)
        {
            await using var payment = db.CreateCommand("INSERT INTO ordering.order_payments(id,order_id,payment_method,payment_status) VALUES($1,$2,$3,$4)");
            payment.Parameters.AddWithValue(Guid.NewGuid());
            payment.Parameters.AddWithValue(orderId);
            payment.Parameters.AddWithValue(paymentMethod);
            payment.Parameters.AddWithValue(paymentStatus!);
            await payment.ExecuteNonQueryAsync();
        }
        return orderId;
    }

    private static async Task<string> OrderStatus(NpgsqlDataSource db, Guid orderId)
    {
        await using var cmd = db.CreateCommand("SELECT status FROM ordering.orders WHERE id=$1");
        cmd.Parameters.AddWithValue(orderId);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<string> PaymentStatus(NpgsqlDataSource db, Guid orderId)
    {
        await using var cmd = db.CreateCommand("SELECT payment_status FROM ordering.order_payments WHERE order_id=$1");
        cmd.Parameters.AddWithValue(orderId);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private static int? Status(IResult result) => (result as IStatusCodeHttpResult)?.StatusCode;
}
