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

    [Fact(DisplayName = "Admin_CanViewInventory")]
    public async Task Admin_CanViewInventory()
    {
        var category = await SeedCategoryAsync();
        var product = await SeedProductAsync("TestProduct", 50, category);

        var admin = CreateClientForRole("Admin");
        var response = await admin.GetAsync("/api/inventory");

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

    private async Task<Product> SeedProductAsync(string name, int stock, Category category)
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
