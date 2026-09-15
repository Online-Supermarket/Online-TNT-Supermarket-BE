using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using TNT.UserService.Api.DTOs;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace TNT.UserService.Tests;

/// <summary>
/// Integration tests for the UserProfile controller endpoints.
/// Uses <see cref="UserWebApplicationFactory"/> with an in-memory database.
///
/// Endpoints covered:
///   GET  /api/users/me  — Returns the caller's profile or 404 if not created yet.
///   PUT  /api/users/me  — Creates or updates the caller's profile.
///
/// Auth strategy: JWT tokens are minted locally using the factory's test secret.
/// </summary>
public class UserControllerTests : IClassFixture<UserWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly UserWebApplicationFactory _factory;

    public UserControllerTests(UserWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>Creates an <see cref="HttpClient"/> that carries a JWT for <paramref name="userId"/>.</summary>
    private HttpClient CreateAuthenticatedClient(Guid userId, string role = "Buyer")
    {
        var token = MintToken(userId, role);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string MintToken(Guid userId, string role)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(UserWebApplicationFactory.TestJwtSecretKey));

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Role, role),
            }),
            Expires = DateTime.UtcNow.AddMinutes(15),
            Issuer = UserWebApplicationFactory.TestIssuer,
            Audience = UserWebApplicationFactory.TestAudience,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        };

        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    private static UpdateProfileRequest ValidUpdateRequest(string? displayName = null) => new()
    {
        DisplayName = displayName ?? $"Test User {Guid.NewGuid():N}",
        PhoneNumber = "+94771234567",
        Address = "123 Main Street",
        City = "Colombo",
    };

    // ──────────────────────────────────────────────────────────────────────────
    // GET /api/users/me
    // ──────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "GetMyProfile_Unauthenticated_Returns401")]
    public async Task GetMyProfile_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/users/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "GetMyProfile_WhenProfileDoesNotExist_Returns404")]
    public async Task GetMyProfile_WhenProfileDoesNotExist_Returns404()
    {
        var userId = Guid.NewGuid(); // no profile created for this user
        var client = CreateAuthenticatedClient(userId);

        var response = await client.GetAsync("/api/users/me");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact(DisplayName = "GetMyProfile_WhenProfileExists_Returns200WithProfile")]
    public async Task GetMyProfile_WhenProfileExists_Returns200WithProfile()
    {
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(userId);
        var request = ValidUpdateRequest("Jane Doe");

        // Create profile first
        var put = await client.PutAsJsonAsync("/api/users/me", request);
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        // Now read it back
        var response = await client.GetAsync("/api/users/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var profile = await response.Content.ReadFromJsonAsync<UserProfileResponse>(JsonOpts);
        profile.Should().NotBeNull();
        profile!.IdentityUserId.Should().Be(userId);
        profile.DisplayName.Should().Be("Jane Doe");
        profile.City.Should().Be("Colombo");
    }

    [Fact(DisplayName = "GetMyProfile_IsolatedPerUser_DoesNotReturnOtherUserProfile")]
    public async Task GetMyProfile_IsolatedPerUser_DoesNotReturnOtherUserProfile()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        // Create profile for user A only
        var clientA = CreateAuthenticatedClient(userA);
        await clientA.PutAsJsonAsync("/api/users/me", ValidUpdateRequest("User A"));

        // User B has no profile
        var clientB = CreateAuthenticatedClient(userB);
        var response = await clientB.GetAsync("/api/users/me");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // PUT /api/users/me
    // ──────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "UpdateMyProfile_Unauthenticated_Returns401")]
    public async Task UpdateMyProfile_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync("/api/users/me", ValidUpdateRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "UpdateMyProfile_WhenProfileDoesNotExist_CreatesAndReturns200")]
    public async Task UpdateMyProfile_WhenProfileDoesNotExist_CreatesAndReturns200()
    {
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(userId);
        var request = ValidUpdateRequest("New User");

        var response = await client.PutAsJsonAsync("/api/users/me", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var profile = await response.Content.ReadFromJsonAsync<UserProfileResponse>(JsonOpts);
        profile.Should().NotBeNull();
        profile!.IdentityUserId.Should().Be(userId);
        profile.DisplayName.Should().Be("New User");
        profile.PhoneNumber.Should().Be("+94771234567");
        profile.Address.Should().Be("123 Main Street");
        profile.City.Should().Be("Colombo");
    }

    [Fact(DisplayName = "UpdateMyProfile_WhenProfileExists_UpdatesAndReturns200")]
    public async Task UpdateMyProfile_WhenProfileExists_UpdatesAndReturns200()
    {
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(userId);

        // First upsert
        await client.PutAsJsonAsync("/api/users/me", ValidUpdateRequest("Original Name"));

        // Second upsert — should overwrite
        var updated = ValidUpdateRequest("Updated Name");
        updated.City = "Kandy";
        var response = await client.PutAsJsonAsync("/api/users/me", updated);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var profile = await response.Content.ReadFromJsonAsync<UserProfileResponse>(JsonOpts);
        profile!.DisplayName.Should().Be("Updated Name");
        profile.City.Should().Be("Kandy");
    }

    [Fact(DisplayName = "UpdateMyProfile_ThenGetProfile_ReturnsSameData")]
    public async Task UpdateMyProfile_ThenGetProfile_ReturnsSameData()
    {
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(userId);
        var request = ValidUpdateRequest("Consistent User");

        await client.PutAsJsonAsync("/api/users/me", request);

        var getResponse = await client.GetAsync("/api/users/me");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var profile = await getResponse.Content.ReadFromJsonAsync<UserProfileResponse>(JsonOpts);
        profile!.DisplayName.Should().Be("Consistent User");
        profile.IdentityUserId.Should().Be(userId);
    }

    [Fact(DisplayName = "UpdateMyProfile_DifferentRoles_AllowedForAnyAuthenticatedRole")]
    public async Task UpdateMyProfile_DifferentRoles_AllowedForAnyAuthenticatedRole()
    {
        // All authenticated roles should be able to manage their own profile
        foreach (var role in new[] { "Buyer", "Admin", "Staff", "Rider" })
        {
            var userId = Guid.NewGuid();
            var client = CreateAuthenticatedClient(userId, role);

            var response = await client.PutAsJsonAsync("/api/users/me", ValidUpdateRequest($"{role} User"));

            response.StatusCode.Should().Be(HttpStatusCode.OK,
                because: $"role '{role}' should be allowed to upsert their own profile");
        }
    }
}
