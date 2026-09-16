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

    [Fact(DisplayName = "Product_CanBeAssociatedWithValidCategory")]
    public async Task Product_CanBeAssociatedWithValidCategory()
    {
        var category = await SeedCategoryAsync();
        var client = CreateClientForRole("Admin");

        var response = await client.PostAsJsonAsync("/api/products", ValidProductRequest(category.Id));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<ProductResponse>(JsonOptions);
        body!.CategoryId.Should().Be(category.Id);
        body.Category.Should().Be(category.Name);
    }

    [Fact(DisplayName = "Product_CannotUseInvalidCategory")]
    public async Task Product_CannotUseInvalidCategory()
    {
        var client = CreateClientForRole("Admin");

        var response = await client.PostAsJsonAsync("/api/products", ValidProductRequest(Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "Customer_CannotCreateProduct")]
    public async Task Customer_CannotCreateProduct()
    {
        var client = CreateBuyerClient();

        var response = await client.PostAsJsonAsync("/api/products", ValidProductRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact(DisplayName = "Staff_CanCreateProduct")]
    public async Task Staff_CanCreateProduct()
    {
        var category = await SeedCategoryAsync();
        var client = CreateClientForRole("Staff");

        var response = await client.PostAsJsonAsync("/api/products", ValidProductRequest(category.Id));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact(DisplayName = "Admin_CanUpdateProduct")]
    public async Task Admin_CanUpdateProduct()
    {
        var category = await SeedCategoryAsync();
        var admin = CreateClientForRole("Admin");

        var createResponse = await admin.PostAsJsonAsync("/api/products", ValidProductRequest(category.Id));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<ProductResponse>(JsonOptions);

        var updateRequest = new ProductRequest
        {
            CategoryId = category.Id,
            Name = "Updated Product Name",
            Description = "Updated Description",
            Price = 250.50m,
            StockQuantity = 45,
            Unit = "kg",
            ImageUrl = "https://example.com/updated.jpg",
            IsActive = true
        };

        var updateResponse = await admin.PutAsJsonAsync($"/api/products/{created!.Id}", updateRequest);

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResponse.Content.ReadFromJsonAsync<ProductResponse>(JsonOptions);
        updated!.Name.Should().Be("Updated Product Name");
        updated.Description.Should().Be("Updated Description");
        updated.Price.Should().Be(250.50m);
        updated.StockQuantity.Should().Be(45);
        updated.Unit.Should().Be("kg");
        updated.ImageUrl.Should().Be("https://example.com/updated.jpg");
        updated.Available.Should().BeTrue();
    }

    [Fact(DisplayName = "Admin_CanDeleteProduct_AndItDisappearsFromCatalog")]
    public async Task Admin_CanDeleteProduct_AndItDisappearsFromCatalog()
    {
        var category = await SeedCategoryAsync();
        var admin = CreateClientForRole("Admin");

        var createResponse = await admin.PostAsJsonAsync("/api/products", ValidProductRequest(category.Id));
        var created = await createResponse.Content.ReadFromJsonAsync<ProductResponse>(JsonOptions);

        var deleteResponse = await admin.DeleteAsync($"/api/products/{created!.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Get by ID should return NotFound
        var getResponse = await _factory.CreateClient().GetAsync($"/api/products/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact(DisplayName = "Customer_CannotUpdateOrDeleteProduct")]
    public async Task Customer_CannotUpdateOrDeleteProduct()
    {
        var category = await SeedCategoryAsync();
        var admin = CreateClientForRole("Admin");
        var customer = CreateBuyerClient();

        var createResponse = await admin.PostAsJsonAsync("/api/products", ValidProductRequest(category.Id));
        var created = await createResponse.Content.ReadFromJsonAsync<ProductResponse>(JsonOptions);

        var updateResponse = await customer.PutAsJsonAsync($"/api/products/{created!.Id}", ValidProductRequest(category.Id));
        updateResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var deleteResponse = await customer.DeleteAsync($"/api/products/{created.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact(DisplayName = "Anonymous_CannotCreateOrUpdateOrDeleteProduct")]
    public async Task Anonymous_CannotCreateOrUpdateOrDeleteProduct()
    {
        var anonymous = _factory.CreateClient();

        var createResponse = await anonymous.PostAsJsonAsync("/api/products", ValidProductRequest());
        createResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var updateResponse = await anonymous.PutAsJsonAsync($"/api/products/{Guid.NewGuid()}", ValidProductRequest());
        updateResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var deleteResponse = await anonymous.DeleteAsync($"/api/products/{Guid.NewGuid()}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "InvalidProductData_ReturnsBadRequest")]
    public async Task InvalidProductData_ReturnsBadRequest()
    {
        var admin = CreateClientForRole("Admin");

        var invalidRequest = new ProductRequest
        {
            Name = "  ",
            Price = -5,
            StockQuantity = -1
        };

        var response = await admin.PostAsJsonAsync("/api/products", invalidRequest);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "NonExistentProduct_ReturnsNotFound")]
    public async Task NonExistentProduct_ReturnsNotFound()
    {
        var admin = CreateClientForRole("Admin");
        var nonExistentId = Guid.NewGuid();

        var getResponse = await admin.GetAsync($"/api/products/{nonExistentId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var updateResponse = await admin.PutAsJsonAsync($"/api/products/{nonExistentId}", ValidProductRequest());
        updateResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var deleteResponse = await admin.DeleteAsync($"/api/products/{nonExistentId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private HttpClient CreateBuyerClient()
    {
        return CreateClientForRole("Buyer");
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

    private async Task SeedProductsAsync(string prefix)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
        var drinks = new Category { Id = Guid.NewGuid(), Name = $"{prefix}-Drinks", IsActive = true, CreatedAt = DateTime.UtcNow };
        var groceries = new Category { Id = Guid.NewGuid(), Name = $"{prefix}-Groceries", IsActive = true, CreatedAt = DateTime.UtcNow };
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

    private async Task<Category> SeedCategoryAsync(bool isActive = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
        var category = new Category
        {
            Id = Guid.NewGuid(),
            Name = $"Category-{Guid.NewGuid():N}",
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow
        };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return category;
    }

    private static ProductRequest ValidProductRequest(Guid? categoryId = null) => new()
    {
        CategoryId = categoryId,
        Name = $"Product-{Guid.NewGuid():N}",
        Description = "Test product",
        Price = 100,
        StockQuantity = 10,
        Unit = "item",
        IsActive = true
    };
}
