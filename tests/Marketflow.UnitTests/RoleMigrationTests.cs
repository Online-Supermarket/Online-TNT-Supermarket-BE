using Xunit;

public class RoleMigrationTests
{
    private static readonly IReadOnlyDictionary<string, string> Legacy = new Dictionary<string, string>
    {
        ["OperationsAdmin"] = "Admin", ["CatalogStaff"] = "Staff", ["InventoryStaff"] = "Staff",
        ["Dispatcher"] = "Staff", ["Courier"] = "Rider", ["DeliveryDriver"] = "Rider"
    };

    private static string[] Normalize(string[]? roles) => (roles ?? [])
        .Select(r => Legacy.TryGetValue(r, out var mapped) ? mapped : r)
        .Where(r => r is "Admin" or "Staff" or "Rider" or "Customer")
        .Distinct(StringComparer.Ordinal).OrderBy(r => r).ToArray();

    [Theory]
    [InlineData("OperationsAdmin", "Admin")]
    [InlineData("CatalogStaff", "Staff")]
    [InlineData("InventoryStaff", "Staff")]
    [InlineData("Dispatcher", "Staff")]
    [InlineData("Courier", "Rider")]
    [InlineData("DeliveryDriver", "Rider")]
    public void Maps_each_legacy_role(string legacy, string canonical) => Assert.Equal([canonical], Normalize([legacy]));

    [Fact] public void Preserves_canonical_and_mixed_roles() => Assert.Equal(["Admin", "Customer", "Staff"], Normalize(["OperationsAdmin", "Customer", "CatalogStaff"]));
    [Fact] public void Unknown_role_gets_no_permission() => Assert.Empty(Normalize(["Unknown"]));
    [Fact] public void Empty_and_null_roles_get_no_permission() { Assert.Empty(Normalize([])); Assert.Empty(Normalize(null)); }
}
