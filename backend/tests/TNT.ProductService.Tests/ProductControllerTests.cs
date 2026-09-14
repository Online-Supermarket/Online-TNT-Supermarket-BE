using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using TNT.ProductService.Api.Data;
using TNT.ProductService.Api.DTOs;
using TNT.ProductService.Api.Entities;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace TNT.ProductService.Tests;

public class ProductControllerTests : IClassFixture<ProductWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly ProductWebApplicationFactory _factory;

    public ProductControllerTests(ProductWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CombinedFilters_ReturnMatchingProducts()
    {
        var prefix = Guid.NewGuid().ToString("N");
        await SeedProductsAsync(prefix);
        var client = CreateBuyerClient();

        var response = await client.GetAsync($"/api/products?search={prefix}-milk&category={prefix}-Drinks&minPrice=100&maxPrice=1000&available=true&sortBy=price&sortOrder=asc&page=1&pageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ProductListResponse>(JsonOptions);
        body!.TotalItems.Should().Be(1);
        body.TotalPages.Should().Be(1);
        body.Items.Single().Name.Should().Be($"{prefix}-milk");
        body.Items.Single().Available.Should().BeTrue();
    }

    [Fact]
    public async Task SortingAndPagination_ReturnsRequestedPage()
    {
        var prefix = Guid.NewGuid().ToString("N");
        await SeedProductsAsync(prefix);
        var client = CreateBuyerClient();

        var response = await client.GetAsync($"/api/products?search={prefix}&sortBy=price&sortOrder=desc&page=2&pageSize=1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ProductListResponse>(JsonOptions);
        body!.TotalItems.Should().Be(3);
        body.TotalPages.Should().Be(3);
        body.Page.Should().Be(2);
        body.PageSize.Should().Be(1);
        body.Items.Single().Price.Should().Be(800);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("minPrice=-1")]
    [InlineData("minPrice=900&maxPrice=100")]
    [InlineData("sortBy=stockQuantity")]
    [InlineData("sortOrder=sideways")]
    public async Task InvalidFilters_ReturnBadRequest(string query)
    {
        var response = await CreateBuyerClient().GetAsync($"/api/products?{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AnyoneCanReadProducts()
    {
        var buyerResponse = await CreateBuyerClient().GetAsync("/api/products");
        var anonymousResponse = await _factory.CreateClient().GetAsync("/api/products");

        buyerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private HttpClient CreateBuyerClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateJwt("Buyer"));
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

    private async Task SeedProductsAsync(string prefix)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
        var drinks = new Category { Id = Guid.NewGuid(), Name = $"{prefix}-Drinks", IsActive = true, CreatedAtUtc = DateTime.UtcNow };
        var groceries = new Category { Id = Guid.NewGuid(), Name = $"{prefix}-Groceries", IsActive = true, CreatedAtUtc = DateTime.UtcNow };
        db.Categories.AddRange(drinks, groceries);
        db.Products.AddRange(
            Product(prefix + "-milk", 500, 20, drinks),
            Product(prefix + "-milk-powder", 800, 0, groceries),
            Product(prefix + "-juice", 1200, 5, drinks));
        await db.SaveChangesAsync();
    }

    private static Product Product(string name, decimal price, int stock, Category category) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Description = $"Description for {name}",
        Price = price,
        StockQuantity = stock,
        Unit = "item",
        IsActive = true,
        Category = category,
        CategoryId = category.Id,
        CreatedAtUtc = DateTime.UtcNow
    };
}