using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TNT.ProductService.Api.Data;

/// <summary>
/// Creates the schema-owner context used only by explicit EF migration commands.
/// Runtime services must continue to use ConnectionStrings:ProductDb.
/// </summary>
public sealed class ProductDbContextFactory : IDesignTimeDbContextFactory<ProductDbContext>
{
    public ProductDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "ConnectionStrings__ProductDbMigrator");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings__ProductDbMigrator is required for EF migration commands.");
        }

        var options = new DbContextOptionsBuilder<ProductDbContext>()
            .UseNpgsql(connectionString, postgres => postgres.CommandTimeout(60))
            .Options;

        return new ProductDbContext(options);
    }
}
