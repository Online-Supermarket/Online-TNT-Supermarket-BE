using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TNT.ProductService.Api.Data;

namespace TNT.ProductService.Tests;

public class ProductWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestJwtSecretKey = "test-secret-key-for-product-tests-32chars!";
    public const string TestIssuer = "TNT.IdentityService";
    public const string TestAudience = "TNT.Supermarket";
    private readonly InMemoryDatabaseRoot _databaseRoot = new();
    private readonly string _databaseName = $"ProductTestDb_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = TestIssuer,
                ["Jwt:Audience"] = TestAudience,
                ["Jwt:SecretKey"] = TestJwtSecretKey,
                ["Cors:AllowedOrigin"] = "http://localhost:5173",
                ["ConnectionStrings:ProductDb"] = "InMemory"
            });
        });

        builder.ConfigureServices(services =>
        {
            var dbDescriptor = services.SingleOrDefault(
                descriptor => descriptor.ServiceType == typeof(DbContextOptions<ProductDbContext>));
            if (dbDescriptor != null) services.Remove(dbDescriptor);

            services.AddDbContext<ProductDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName, _databaseRoot));

            services.PostConfigure<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>(
                Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme,
                options =>
                {
                    options.TokenValidationParameters.IssuerSigningKey =
                        new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                            System.Text.Encoding.UTF8.GetBytes(TestJwtSecretKey));
                });
        });
    }
}
