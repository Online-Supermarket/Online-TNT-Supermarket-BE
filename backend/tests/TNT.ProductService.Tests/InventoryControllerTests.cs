using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using TNT.ProductService.Api.Data;
using TNT.ProductService.Api.DTOs;
using TNT.ProductService.Api.Entities;

namespace TNT.ProductService.Tests;

public class InventoryControllerTests : IClassFixture<ProductWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly ProductWebApplicationFactory _factory;

    public InventoryControllerTests(ProductWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ── Existing tests (preserved) ────────────────────────────────────────────────

    [Fact(DisplayName = "Admin_CanViewInventory")]
    public async Task Admin_CanViewInventory()
    {
        var category = await SeedCategoryAsync();
        var product = await SeedProductAsync("TestProduct", 50, category);

        var admin = CreateClientForRole("Admin");
        var response = await admin.GetAsync($"/api/inventory?search={product.Name}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<InventoryListResponse>(JsonOptions);
        body!.Items.Should().Contain(i => i.ProductId == product.Id && i.StockQuantity == 50);
    }

    [Fact(DisplayName = "Customer_CannotViewInventory")]
    public async Task Customer_CannotViewInventory()
    {
        var customer = CreateClientForRole("Buyer");
        var response = await customer.GetAsync("/api/inventory");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact(DisplayName = "Admin_CanUpdateInventory")]
    public async Task Admin_CanUpdateInventory()
    {
        var category = await SeedCategoryAsync();
        var product = await SeedProductAsync("UpdateTest", 10, category);

        var admin = CreateClientForRole("Admin");
        var updateRequest = new InventoryUpdateRequest { StockQuantity = 100 };

        var response = await admin.PutAsJsonAsync($"/api/inventory/{product.Id}", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<InventoryResponse>(JsonOptions);
        body!.StockQuantity.Should().Be(100);

        // Verify it was updated in the DB
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
        var dbProduct = await db.Products.FindAsync(product.Id);
        dbProduct!.StockQuantity.Should().Be(100);
    }

    [Fact(DisplayName = "Admin_CannotSetNegativeInventory")]
    public async Task Admin_CannotSetNegativeInventory()
    {
        var category = await SeedCategoryAsync();
        var product = await SeedProductAsync("NegativeTest", 10, category);

        var admin = CreateClientForRole("Admin");
        var updateRequest = new InventoryUpdateRequest { StockQuantity = -5 };

        var response = await admin.PutAsJsonAsync($"/api/inventory/{product.Id}", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "Staff_CanUpdateInventory")]
    public async Task Staff_CanUpdateInventory()
    {
        var category = await SeedCategoryAsync();
        var product = await SeedProductAsync("StaffUpdateTest", 15, category);

        var staff = CreateClientForRole("Staff");
        var updateRequest = new InventoryUpdateRequest { StockQuantity = 20 };

        var response = await staff.PutAsJsonAsync($"/api/inventory/{product.Id}", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "Update_NonExistentProduct_ReturnsNotFound")]
    public async Task Update_NonExistentProduct_ReturnsNotFound()
    {
        var admin = CreateClientForRole("Admin");
        var updateRequest = new InventoryUpdateRequest { StockQuantity = 50 };

        var response = await admin.PutAsJsonAsync($"/api/inventory/{Guid.NewGuid()}", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── New tests: POST (Add Inventory) ────────────────────────────────────────────

    [Fact(DisplayName = "Admin_CanAddInventory")]
    public async Task Admin_CanAddInventory()
    {
        var category = await SeedCategoryAsync();
        // Stock seeded at 0 = no inventory yet
        var product = await SeedProductAsync("AddInventoryAdmin", 0, category);

        var admin = CreateClientForRole("Admin");
        var addRequest = new InventoryAddRequest { ProductId = product.Id, StockQuantity = 75 };

        var response = await admin.PostAsJsonAsync("/api/inventory", addRequest);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<InventoryResponse>(JsonOptions);
        body!.ProductId.Should().Be(product.Id);
        body.StockQuantity.Should().Be(75);

        // Verify the DB was updated
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
        var dbProduct = await db.Products.FindAsync(product.Id);
        dbProduct!.StockQuantity.Should().Be(75);
    }

    [Fact(DisplayName = "Staff_CanAddInventory")]
    public async Task Staff_CanAddInventory()
    {
        var category = await SeedCategoryAsync();
        var product = await SeedProductAsync("AddInventoryStaff", 0, category);

        var staff = CreateClientForRole("Staff");
        var addRequest = new InventoryAddRequest { ProductId = product.Id, StockQuantity = 30 };

        var response = await staff.PostAsJsonAsync("/api/inventory", addRequest);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<InventoryResponse>(JsonOptions);
        body!.StockQuantity.Should().Be(30);
    }

    [Fact(DisplayName = "Add_NegativeStock_ReturnsBadRequest")]
    public async Task Add_NegativeStock_ReturnsBadRequest()
    {
        var category = await SeedCategoryAsync();
        var product = await SeedProductAsync("NegAddTest", 0, category);

        var admin = CreateClientForRole("Admin");
        // StockQuantity -1 violates [Range(0, int.MaxValue)] → model-state 400
        var addRequest = new { productId = product.Id, stockQuantity = -1 };

        var response = await admin.PostAsJsonAsync("/api/inventory", addRequest);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "Add_NonExistentProduct_ReturnsNotFound")]
    public async Task Add_NonExistentProduct_ReturnsNotFound()
    {
        var admin = CreateClientForRole("Admin");
        var addRequest = new InventoryAddRequest { ProductId = Guid.NewGuid(), StockQuantity = 10 };

        var response = await admin.PostAsJsonAsync("/api/inventory", addRequest);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact(DisplayName = "Add_DuplicateInventory_ReturnsConflict")]
    public async Task Add_DuplicateInventory_ReturnsConflict()
    {
        var category = await SeedCategoryAsync();
        // Stock already set to 50 — this product already "has inventory"
        var product = await SeedProductAsync("DuplicateInvTest", 50, category);

        var admin = CreateClientForRole("Admin");
        var addRequest = new InventoryAddRequest { ProductId = product.Id, StockQuantity = 100 };

        var response = await admin.PostAsJsonAsync("/api/inventory", addRequest);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ── New tests: DELETE (Reset Inventory) ───────────────────────────────────────

    [Fact(DisplayName = "Admin_CanDeleteInventory")]
    public async Task Admin_CanDeleteInventory()
    {
        var category = await SeedCategoryAsync();
        var product = await SeedProductAsync("DeleteInvTest", 40, category);

        var admin = CreateClientForRole("Admin");
        var response = await admin.DeleteAsync($"/api/inventory/{product.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Confirm stock was reset to 0 in the DB
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
        var dbProduct = await db.Products.FindAsync(product.Id);
        dbProduct!.StockQuantity.Should().Be(0);
    }

    [Fact(DisplayName = "Delete_NonExistentProduct_ReturnsNotFound")]
    public async Task Delete_NonExistentProduct_ReturnsNotFound()
    {
        var admin = CreateClientForRole("Admin");
        var response = await admin.DeleteAsync($"/api/inventory/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── New tests: Customer RBAC (Scenario 4) ─────────────────────────────────────

    [Fact(DisplayName = "Customer_CannotAddInventory")]
    public async Task Customer_CannotAddInventory()
    {
        var category = await SeedCategoryAsync();
        var product = await SeedProductAsync("CustomerAddTest", 0, category);

        var customer = CreateClientForRole("Buyer");
        var addRequest = new InventoryAddRequest { ProductId = product.Id, StockQuantity = 10 };

        var response = await customer.PostAsJsonAsync("/api/inventory", addRequest);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact(DisplayName = "Customer_CannotUpdateInventory")]
    public async Task Customer_CannotUpdateInventory()
    {
        var category = await SeedCategoryAsync();
        var product = await SeedProductAsync("CustomerUpdateTest", 5, category);

        var customer = CreateClientForRole("Buyer");
        var updateRequest = new InventoryUpdateRequest { StockQuantity = 999 };

        var response = await customer.PutAsJsonAsync($"/api/inventory/{product.Id}", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact(DisplayName = "Customer_CannotDeleteInventory")]
    public async Task Customer_CannotDeleteInventory()
    {
        var category = await SeedCategoryAsync();
        var product = await SeedProductAsync("CustomerDeleteTest", 20, category);

        var customer = CreateClientForRole("Buyer");
        var response = await customer.DeleteAsync($"/api/inventory/{product.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── New tests: Unauthenticated access ─────────────────────────────────────────

    [Fact(DisplayName = "Unauthenticated_CannotViewInventory")]
    public async Task Unauthenticated_CannotViewInventory()
    {
        var anonymous = _factory.CreateClient();  // no token
        var response = await anonymous.GetAsync("/api/inventory");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "Unauthenticated_CannotAddInventory")]
    public async Task Unauthenticated_CannotAddInventory()
    {
        var anonymous = _factory.CreateClient();
        var addRequest = new InventoryAddRequest { ProductId = Guid.NewGuid(), StockQuantity = 10 };

        var response = await anonymous.PostAsJsonAsync("/api/inventory", addRequest);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Stock Level Management Tests ──────────────────────────────────────────────

    [Fact(DisplayName = "Admin_CanIncreaseStock_UsingPatch")]
    public async Task Admin_CanIncreaseStock_UsingPatch()
    {
        var category = await SeedCategoryAsync();
        var product = await SeedProductAsync("IncreaseTest", 50, category);

        var admin = CreateClientForRole("Admin");
        var adjustRequest = new StockAdjustRequest { Type = "increase", Quantity = 20 };

        var response = await admin.PatchAsJsonAsync($"/api/inventory/{product.Id}/stock", adjustRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<InventoryResponse>(JsonOptions);
        body!.StockQuantity.Should().Be(70);

        // Verify DB
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
        var dbProduct = await db.Products.FindAsync(product.Id);
        dbProduct!.StockQuantity.Should().Be(70);
    }

    [Fact(DisplayName = "Staff_CanDecreaseStock_UsingPatch")]
    public async Task Staff_CanDecreaseStock_UsingPatch()
    {
        var category = await SeedCategoryAsync();
        var product = await SeedProductAsync("DecreaseTest", 50, category);

        var staff = CreateClientForRole("Staff");
        var adjustRequest = new StockAdjustRequest { Type = "decrease", Quantity = 20 };

        var response = await staff.PatchAsJsonAsync($"/api/inventory/{product.Id}/stock", adjustRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<InventoryResponse>(JsonOptions);
        body!.StockQuantity.Should().Be(30);

        // Verify DB
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
        var dbProduct = await db.Products.FindAsync(product.Id);
        dbProduct!.StockQuantity.Should().Be(30);
    }

    [Fact(DisplayName = "PreventNegativeStock_ReturnsBadRequest_AndStockUnchanged")]
    public async Task PreventNegativeStock_ReturnsBadRequest_AndStockUnchanged()
    {
        var category = await SeedCategoryAsync();
        var product = await SeedProductAsync("NegStockTest", 10, category);

        var admin = CreateClientForRole("Admin");
        var adjustRequest = new StockAdjustRequest { Type = "decrease", Quantity = 15 };

        var response = await admin.PatchAsJsonAsync($"/api/inventory/{product.Id}/stock", adjustRequest);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("Insufficient stock available.");

        // Database remains unchanged at 10
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
        var dbProduct = await db.Products.FindAsync(product.Id);
        dbProduct!.StockQuantity.Should().Be(10);
    }

    [Fact(DisplayName = "Admin_CanAdjustStock_UsingChangeDelta")]
    public async Task Admin_CanAdjustStock_UsingChangeDelta()
    {
        var category = await SeedCategoryAsync();
        var product = await SeedProductAsync("ChangeDeltaTest", 40, category);

        var admin = CreateClientForRole("Admin");
        var response1 = await admin.PatchAsJsonAsync($"/api/inventory/{product.Id}/stock", new StockAdjustRequest { Change = 15 });
        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        var body1 = await response1.Content.ReadFromJsonAsync<InventoryResponse>(JsonOptions);
        body1!.StockQuantity.Should().Be(55);

        var response2 = await admin.PatchAsJsonAsync($"/api/inventory/{product.Id}/stock", new StockAdjustRequest { Change = -20 });
        response2.StatusCode.Should().Be(HttpStatusCode.OK);
        var body2 = await response2.Content.ReadFromJsonAsync<InventoryResponse>(JsonOptions);
        body2!.StockQuantity.Should().Be(35);
    }

    [Fact(DisplayName = "Admin_CanSetStockAndThreshold_UsingPatch")]
    public async Task Admin_CanSetStockAndThreshold_UsingPatch()
    {
        var category = await SeedCategoryAsync();
        var product = await SeedProductAsync("SetTest", 20, category, lowStockThreshold: 10);

        var admin = CreateClientForRole("Admin");
        var adjustRequest = new StockAdjustRequest { Type = "set", Quantity = 80, LowStockThreshold = 25 };

        var response = await admin.PatchAsJsonAsync($"/api/inventory/{product.Id}/stock", adjustRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<InventoryResponse>(JsonOptions);
        body!.StockQuantity.Should().Be(80);
        body.LowStockThreshold.Should().Be(25);
        body.IsLowStock.Should().BeFalse(); // 80 is not < 25
    }

    [Fact(DisplayName = "LowStock_Detection_ReturnsCorrectIsLowStock")]
    public async Task LowStock_Detection_ReturnsCorrectIsLowStock()
    {
        var category = await SeedCategoryAsync();
        var lowProduct = await SeedProductAsync("LowStockItem", 5, category, lowStockThreshold: 10);
        var normalProduct = await SeedProductAsync("NormalStockItem", 15, category, lowStockThreshold: 10);

        var staff = CreateClientForRole("Staff");

        var responseLow = await staff.GetAsync($"/api/inventory/{lowProduct.Id}");
        responseLow.StatusCode.Should().Be(HttpStatusCode.OK);
        var bodyLow = await responseLow.Content.ReadFromJsonAsync<InventoryResponse>(JsonOptions);
        bodyLow!.StockQuantity.Should().Be(5);
        bodyLow.LowStockThreshold.Should().Be(10);
        bodyLow.IsLowStock.Should().BeTrue();

        var responseNormal = await staff.GetAsync($"/api/inventory/{normalProduct.Id}");
        responseNormal.StatusCode.Should().Be(HttpStatusCode.OK);
        var bodyNormal = await responseNormal.Content.ReadFromJsonAsync<InventoryResponse>(JsonOptions);
        bodyNormal!.StockQuantity.Should().Be(15);
        bodyNormal.LowStockThreshold.Should().Be(10);
        bodyNormal.IsLowStock.Should().BeFalse();
    }

    [Fact(DisplayName = "LowStock_Filter_ReturnsOnlyLowStockProducts")]
    public async Task LowStock_Filter_ReturnsOnlyLowStockProducts()
    {
        var category = await SeedCategoryAsync();
        var lowItem = await SeedProductAsync("FilterLowItem", 3, category, lowStockThreshold: 10);
        var normalItem = await SeedProductAsync("FilterNormalItem", 50, category, lowStockThreshold: 10);

        var staff = CreateClientForRole("Staff");
        var response = await staff.GetAsync("/api/inventory?lowStock=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<InventoryListResponse>(JsonOptions);
        body!.Items.Should().Contain(i => i.ProductId == lowItem.Id);
        body.Items.Should().NotContain(i => i.ProductId == normalItem.Id);
    }

    [Fact(DisplayName = "Customer_CannotAdjustStock_ReturnsForbidden")]
    public async Task Customer_CannotAdjustStock_ReturnsForbidden()
    {
        var category = await SeedCategoryAsync();
        var product = await SeedProductAsync("CustomerAdjust", 20, category);

        var customer = CreateClientForRole("Buyer");
        var adjustRequest = new StockAdjustRequest { Type = "increase", Quantity = 10 };

        var response = await customer.PatchAsJsonAsync($"/api/inventory/{product.Id}/stock", adjustRequest);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact(DisplayName = "Unauthenticated_CannotAdjustStock_ReturnsUnauthorized")]
    public async Task Unauthenticated_CannotAdjustStock_ReturnsUnauthorized()
    {
        var anonymous = _factory.CreateClient();
        var adjustRequest = new StockAdjustRequest { Type = "increase", Quantity = 10 };

        var response = await anonymous.PatchAsJsonAsync($"/api/inventory/{Guid.NewGuid()}/stock", adjustRequest);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "AdjustStock_NonExistentProduct_ReturnsNotFound")]
    public async Task AdjustStock_NonExistentProduct_ReturnsNotFound()
    {
        var admin = CreateClientForRole("Admin");
        var adjustRequest = new StockAdjustRequest { Type = "increase", Quantity = 10 };

        var response = await admin.PatchAsJsonAsync($"/api/inventory/{Guid.NewGuid()}/stock", adjustRequest);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private HttpClient CreateClientForRole(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateJwt(role));
        return client;
    }

    private static string CreateJwt(string role)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, role)
        };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ProductWebApplicationFactory.TestJwtSecretKey));
        var token = new JwtSecurityToken(
            issuer: ProductWebApplicationFactory.TestIssuer,
            audience: ProductWebApplicationFactory.TestAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(20),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private async Task<Category> SeedCategoryAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
        var category = new Category
        {
            Id = Guid.NewGuid(),
            Name = $"Category-{Guid.NewGuid():N}",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return category;
    }

    private async Task<Product> SeedProductAsync(string name, int stock, Category category, int lowStockThreshold = 10)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();

        // Attach category to context if not already attached
        db.Categories.Attach(category);

        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = "Test product",
            Price = 100,
            StockQuantity = stock,
            LowStockThreshold = lowStockThreshold,
            Unit = "item",
            IsActive = true,
            CategoryId = category.Id,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product;
    }
}
