using System;
using System.Text.Json;
record SalesRow(Guid Id, string PaymentMethod);
class Program {
    static void Main() {
        var row = new SalesRow(Guid.NewGuid(), "BankTransfer");
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        Console.WriteLine(JsonSerializer.Serialize(row, options));
        Console.WriteLine(JsonSerializer.Serialize(row));
    }
}
