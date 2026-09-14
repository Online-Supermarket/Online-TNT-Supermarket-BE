using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using TNT.ProductService.Api.DTOs;

namespace TNT.ProductService.Tests;

public class CategoryControllerTests : IClassFixture<ProductWebApplicationFactory>
{
    private readonly ProductWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public CategoryControllerTests(ProductWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact(DisplayName = "Admin_CanCreateCategory")]
    public async Task Admin_CanCreateCategory()
    {
        var client = CreateClientForRole("Admin");
        var request = ValidRequest();

        var response = await client.PostAsJsonAsync("/api/categories", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CategoryResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.Name.Should().Be(request.Name);
        body.IsActive.Should().BeTrue();
    }

    [Fact(DisplayName = "Staff_CanCreateCategory")]
    public async Task Staff_CanCreateCategory()
    {
        var client = CreateClientForRole("Staff");

        var response = await client.PostAsJsonAsync("/api/categories", ValidRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact(DisplayName = "Admin_CanUpdateCategory")]
    public async Task Admin_CanUpdateCategory()
    {
        var client = CreateClientForRole("Admin");
        var category = await CreateCategoryAsync(client);
        var update = ValidRequest();
        update.Name = $"{category.Name} Updated";
        update.Description = "Updated category";
        update.IsActive = false;

        var response = await client.PutAsJsonAsync($"/api/categories/{category.Id}", update);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CategoryResponse>(JsonOptions);
        body!.Name.Should().Be(update.Name);
        body.Description.Should().Be("Updated category");
        body.IsActive.Should().BeFalse();
        body.UpdatedAtUtc.Should().NotBeNull();
    }

    [Fact(DisplayName = "Staff_CanUpdateCategory")]
    public async Task Staff_CanUpdateCategory()
    {
        var admin = CreateClientForRole("Admin");
        var category = await CreateCategoryAsync(admin);
        var staff = CreateClientForRole("Staff");

        var update = ValidRequest();
        update.Name = $"{category.Name} Staff Updated";
        var response = await staff.PutAsJsonAsync($"/api/categories/{category.Id}", update);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "Categories_CanBeListedAndRetrievedById")]
    public async Task Categories_CanBeListedAndRetrievedById()
    {
        var admin = CreateClientForRole("Admin");
        var category = await CreateCategoryAsync(admin);
        var anonymous = _factory.CreateClient();

        var listResponse = await anonymous.GetAsync("/api/categories");
        var getResponse = await anonymous.GetAsync($"/api/categories/{category.Id}");

        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await listResponse.Content.ReadFromJsonAsync<CategoryListResponse>(JsonOptions);
        list!.Items.Should().Contain(item => item.Id == category.Id);

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var retrieved = await getResponse.Content.ReadFromJsonAsync<CategoryResponse>(JsonOptions);
        retrieved!.Id.Should().Be(category.Id);
    }

    [Theory(DisplayName = "UnauthorizedRoles_CannotModifyCategories")]
    [InlineData("Buyer")]
    [InlineData("Customer")]
    [InlineData("Rider")]
    [InlineData("Delivery")]
    public async Task UnauthorizedRoles_CannotModifyCategories(string role)
    {
        var admin = CreateClientForRole("Admin");
        var category = await CreateCategoryAsync(admin);
        var client = CreateClientForRole(role);

        var create = await client.PostAsJsonAsync("/api/categories", ValidRequest());
        var update = await client.PutAsJsonAsync($"/api/categories/{category.Id}", ValidRequest());
        var delete = await client.DeleteAsync($"/api/categories/{category.Id}");

        create.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        update.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        delete.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact(DisplayName = "Anonymous_CannotModifyCategories")]
    public async Task Anonymous_CannotModifyCategories()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/categories", ValidRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "InvalidCategory_ReturnsBadRequest")]
    public async Task InvalidCategory_ReturnsBadRequest()
    {
        var client = CreateClientForRole("Admin");
        var request = ValidRequest();
        request.Name = "   ";

        var response = await client.PostAsJsonAsync("/api/categories", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "DuplicateCategory_ReturnsConflict")]
    public async Task DuplicateCategory_ReturnsConflict()
    {
        var client = CreateClientForRole("Admin");
        var request = ValidRequest();
        await CreateCategoryAsync(client, request);

        var response = await client.PostAsJsonAsync("/api/categories", ValidRequest(request.Name));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact(DisplayName = "UnknownCategory_ReturnsNotFound")]
    public async Task UnknownCategory_ReturnsNotFound()
    {
        var client = CreateClientForRole("Admin");

        var get = await client.GetAsync($"/api/categories/{Guid.NewGuid()}");
        var update = await client.PutAsJsonAsync($"/api/categories/{Guid.NewGuid()}", ValidRequest());
        var delete = await client.DeleteAsync($"/api/categories/{Guid.NewGuid()}");

        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
        update.StatusCode.Should().Be(HttpStatusCode.NotFound);
        delete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact(DisplayName = "DeleteCategory_SoftDeactivatesCategory")]
    public async Task DeleteCategory_SoftDeactivatesCategory()
    {
        var client = CreateClientForRole("Admin");
        var category = await CreateCategoryAsync(client);

        var delete = await client.DeleteAsync($"/api/categories/{category.Id}");

        delete.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await delete.Content.ReadFromJsonAsync<CategoryResponse>(JsonOptions);
        body!.IsActive.Should().BeFalse();
    }

    private HttpClient CreateClientForRole(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateJwt(role));
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

    private static CategoryRequest ValidRequest(string? name = null) => new()
    {
        Name = name ?? $"Category-{Guid.NewGuid():N}",
        Description = "Test category",
        ImageUrl = "https://example.com/category.png",
        IsActive = true
    };

    private static async Task<CategoryResponse> CreateCategoryAsync(HttpClient client, CategoryRequest? request = null)
    {
        var response = await client.PostAsJsonAsync("/api/categories", request ?? ValidRequest());
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CategoryResponse>(JsonOptions))!;
    }
}
