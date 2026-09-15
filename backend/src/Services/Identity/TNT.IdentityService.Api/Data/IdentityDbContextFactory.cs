using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TNT.IdentityService.Api.Data;

/// <summary>
/// Creates the schema-owner context used only by explicit EF migration commands.
/// Runtime services must continue to use ConnectionStrings:IdentityDb.
/// </summary>
public sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "ConnectionStrings__IdentityDbMigrator");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings__IdentityDbMigrator is required for EF migration commands.");
        }

        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(connectionString, postgres => postgres.CommandTimeout(60))
            .Options;

        return new IdentityDbContext(options);
    }
}
