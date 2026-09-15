using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TNT.UserService.Api.Data;

namespace TNT.UserService.Tests;

/// <summary>
/// Custom WebApplicationFactory for User Service integration tests.
/// Provides:
/// - InMemory EF Core database (unique per factory instance — no shared state between test classes)
/// - Fully configured JWT settings via in-memory config (no file dependency)
/// </summary>
public class UserWebApplicationFactory : WebApplicationFactory<Program>
{
    // Shared known test secret — same key used in both JWT generation and validation
    public const string TestJwtSecretKey = "test-secret-key-for-user-tests-32chars!!";
    public const string TestIssuer = "TNT.IdentityService";
    public const string TestAudience = "TNT.Supermarket";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Inject all required settings directly — no appsettings file dependency
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"]             = TestIssuer,
                ["Jwt:Audience"]           = TestAudience,
                ["Jwt:SecretKey"]          = TestJwtSecretKey,

                // CORS — not exercised in tests but must be valid
                ["Cors:AllowedOrigin"] = "http://localhost:5173",

                // ConnectionString — overridden by InMemory DB below, but must be non-empty
                ["ConnectionStrings:UserDb"] = "InMemory",
            });
        });

        builder.ConfigureServices(services =>
        {
            // ── Remove the real Postgres DbContext ──────────────────────────
            var dbDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<UserDbContext>));
            if (dbDescriptor != null) services.Remove(dbDescriptor);

            // ── Use a unique in-memory database per factory instance ─────────
            var dbName = $"UserTestDb_{Guid.NewGuid():N}";
            services.AddDbContext<UserDbContext>(opts =>
                opts.UseInMemoryDatabase(dbName));
        });
    }
}
