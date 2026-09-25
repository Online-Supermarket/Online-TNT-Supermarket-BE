public static class IdentityRoles
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { "Admin", "Staff", "Rider", "Customer" };
    // Compatibility is limited to unambiguous legacy names read from existing accounts.
    public static string Normalize(string role) => role switch
    {
        "OperationsAdmin" => "Admin",
        "CatalogStaff" or "InventoryStaff" => "Staff",
        _ => role
    };
    public static string[] NormalizeAll(IEnumerable<string> roles) => roles.Select(Normalize).Where(All.Contains).Distinct(StringComparer.Ordinal).ToArray();
    public static bool IsValidAssignment(string[]? roles) => roles is { Length: > 0 } && roles.All(All.Contains);
    public static bool IsStaffOrAdmin(string role) => Normalize(role) is "Staff" or "Admin";
}
