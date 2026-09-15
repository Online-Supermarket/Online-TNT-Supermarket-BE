using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TNT.UserService.Api.Data;

/// <summary>
/// Creates the schema-owner context used only by explicit EF migration commands.
/// Runtime services must continue to use ConnectionStrings:UserDb.
/// </summary>
public sealed class UserDbContextFactory : IDesignTimeDbContextFactory<UserDbContext>
{
    public UserDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "ConnectionStrings__UserDbMigrator");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings__UserDbMigrator is required for EF migration commands.");
        }

        var options = new DbContextOptionsBuilder<UserDbContext>()
            .UseNpgsql(connectionString, postgres => postgres.CommandTimeout(60))
            .Options;

        return new UserDbContext(options);
    }
}
