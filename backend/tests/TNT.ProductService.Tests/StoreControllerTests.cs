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

public class StoreControllerTests : IClassFixture<ProductWebApplicationFactory>
{
    private readonly ProductWebApplicationFactory _factory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public StoreControllerTests(ProductWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact(DisplayName = "Admin_CanListStores")]
    public async Task Admin_CanListStores()
    {
        var client = CreateClientForRole("Admin");
        await CreateStoreAsync(client);

        var response = await client.GetAsync("/api/stores");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<StoreListResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.Items.Should().NotBeEmpty();
    }

    [Fact(DisplayName = "Admin_CanGetStoreById")]
    public async Task Admin_CanGetStoreById()
    {
        var client = CreateClientForRole("Admin");
        var created = await CreateStoreAsync(client);

        var response = await client.GetAsync($"/api/stores/{created.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<StoreResponse>(JsonOptions);
        body!.Id.Should().Be(created.Id);
        body.StoreCode.Should().Be(created.StoreCode);
    }

    [Fact(DisplayName = "Admin_CanCreateStore")]
    public async Task Admin_CanCreateStore()
    {
        var client = CreateClientForRole("Admin");
        var request = ValidRequest();

        var response = await client.PostAsJsonAsync("/api/stores", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<StoreResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.StoreName.Should().Be(request.StoreName);
        body.Address.City.Should().Be(request.Address.City);
        body.IsActive.Should().BeTrue();
    }

    [Fact(DisplayName = "Admin_CanUpdateStore")]
    public async Task Admin_CanUpdateStore()
    {
        var client = CreateClientForRole("Admin");
        var created = await CreateStoreAsync(client);
        var update = ValidRequest("TNT-KDY-002");
        update.StoreName = "TNT Kandy Updated";
        update.Address.City = "Kandy";
        update.IsActive = false;

        var response = await client.PutAsJsonAsync($"/api/stores/{created.Id}", update);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<StoreResponse>(JsonOptions);
        body!.StoreName.Should().Be("TNT Kandy Updated");
        body.Address.City.Should().Be("Kandy");
        body.IsActive.Should().BeFalse();
    }

    [Fact(DisplayName = "Admin_CanActivateAndDeactivateStore")]
    public async Task Admin_CanActivateAndDeactivateStore()
    {
        var client = CreateClientForRole("Admin");
        var created = await CreateStoreAsync(client);

        var deactivate = await client.PatchAsync($"/api/stores/{created.Id}/deactivate", null);
        deactivate.StatusCode.Should().Be(HttpStatusCode.OK);
        var deactivated = await deactivate.Content.ReadFromJsonAsync<StoreResponse>(JsonOptions);
        deactivated!.IsActive.Should().BeFalse();

        var activate = await client.PatchAsync($"/api/stores/{created.Id}/activate", null);
        activate.StatusCode.Should().Be(HttpStatusCode.OK);
        var activated = await activate.Content.ReadFromJsonAsync<StoreResponse>(JsonOptions);
        activated!.IsActive.Should().BeTrue();

        var retrieved = await client.GetFromJsonAsync<StoreResponse>($"/api/stores/{created.Id}", JsonOptions);
        retrieved!.IsActive.Should().BeTrue();
    }

    [Theory(DisplayName = "NonAdminRoles_ReceiveForbidden_OnStoreModificationApis")]
    [InlineData("Buyer")]
    [InlineData("Customer")]
    [InlineData("Staff")]
    [InlineData("Rider")]
    [InlineData("Delivery")]
    [InlineData("Manager")]
    public async Task NonAdminRoles_ReceiveForbidden_OnStoreModificationApis(string role)
    {
        var adminClient = CreateClientForRole("Admin");
        var store = await CreateStoreAsync(adminClient);
        var client = CreateClientForRole(role);

        var createResponse = await client.PostAsJsonAsync("/api/stores", ValidRequest());
        var updateResponse = await client.PutAsJsonAsync($"/api/stores/{store.Id}", ValidRequest());
        var deactivateResponse = await client.PatchAsync($"/api/stores/{store.Id}/deactivate", null);

        createResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        deactivateResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact(DisplayName = "UnauthenticatedRequest_ReceivesUnauthorized")]
    public async Task UnauthenticatedRequest_ReceivesUnauthorized()
    {
        var client = _factory.CreateClient();

        var getResponse = await client.GetAsync("/api/stores");
        var postResponse = await client.PostAsJsonAsync("/api/stores", ValidRequest());

        getResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        postResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "ValidationFailure_ReturnsBadRequest")]
    public async Task ValidationFailure_ReturnsBadRequest()
    {
        var client = CreateClientForRole("Admin");
        var request = ValidRequest();
        request.StoreName = "";

        var response = await client.PostAsJsonAsync("/api/stores", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "WhitespaceRequiredFields_ReturnBadRequest")]
    public async Task WhitespaceRequiredFields_ReturnBadRequest()
    {
        var client = CreateClientForRole("Admin");
        var request = ValidRequest();
        request.StoreName = "   ";

        var response = await client.PostAsJsonAsync("/api/stores", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "DuplicateStoreCode_ReturnsConflict")]
    public async Task DuplicateStoreCode_ReturnsConflict()
    {
        var client = CreateClientForRole("Admin");
        var request = ValidRequest();
        await CreateStoreAsync(client, request);

        var duplicate = ValidRequest(request.StoreCode);

        var response = await client.PostAsJsonAsync("/api/stores", duplicate);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact(DisplayName = "UnknownStoreId_ReturnsNotFound")]
    public async Task UnknownStoreId_ReturnsNotFound()
    {
        var client = CreateClientForRole("Admin");

        var response = await client.GetAsync($"/api/stores/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact(DisplayName = "UnknownStoreId_OnUpdate_ReturnsNotFound")]
    public async Task UnknownStoreId_OnUpdate_ReturnsNotFound()
    {
        var client = CreateClientForRole("Admin");

        var response = await client.PutAsJsonAsync($"/api/stores/{Guid.NewGuid()}", ValidRequest());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
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
            new Claim(JwtRegisteredClaimNames.Email, $"{role.ToLowerInvariant()}@tnt.com"),
            new Claim(ClaimTypes.Role, role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ProductWebApplicationFactory.TestJwtSecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: ProductWebApplicationFactory.TestIssuer,
            audience: ProductWebApplicationFactory.TestAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(20),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static StoreRequest ValidRequest(string? storeCode = null) => new()
    {
        StoreCode = storeCode ?? CreateStoreCode(),
        StoreName = "TNT Colombo",
        Address = new StoreAddressDto
        {
            Line1 = "No. 10 Main Street",
            Line2 = "Level 1",
            City = "Colombo",
            PostalCode = "00100",
            Country = "Sri Lanka"
        },
        ContactNumber = "+94112345678",
        Email = "colombo@tnt.com",
        IsActive = true
    };

    private static string CreateStoreCode()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        return $"TNT-{suffix}";
    }

    private static async Task<StoreResponse> CreateStoreAsync(HttpClient client, StoreRequest? request = null)
    {
        var response = await client.PostAsJsonAsync("/api/stores", request ?? ValidRequest());
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<StoreResponse>(JsonOptions))!;
    }
}
