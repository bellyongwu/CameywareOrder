using CameywareOrder.Localization;

namespace CameywareOrder.Models;

/// <summary>
/// A named set of capabilities — what the application used to call a role and hard-code.
///
/// One catalog for the whole installation, and a user is given roles PER SHOP. That was a decision,
/// not a default: per-shop role definitions would let "Auditor" mean two different things in two
/// branches, so an administrator reading the word would have to check which shop they were looking
/// at before they knew what it granted.
/// </summary>
/// <remarks>
/// <see cref="Id"/> is what memberships store, so it is a COMPATIBILITY SURFACE — it is generated
/// once from the name and then never changes, which is what lets a role be renamed without every
/// member losing it.
///
/// Built-in roles are editable but not removable: accounts hold them, and the shipped defaults are
/// what a fresh installation is usable with. <see cref="IsAdministratorRole"/> is stricter still —
/// it is the account-level administrator, listed here so the panel can show what an administrator
/// may do, and locked because an administrator has every capability by definition.
/// </remarks>
public sealed class RoleDefinition
{
    /// <summary>The administrator, whose rights are an account flag rather than a membership.</summary>
    public const string AdministratorId = "admin";

    public const string ManagerId = "manager";

    public const string StaffId = "staff";

    /// <summary>The example role the release ships with — see <c>RolePermissionStore</c>.</summary>
    public const string AuditorId = "auditor";

    public RoleDefinition(
        string id,
        string? customName,
        string? nameKey,
        bool isBuiltIn,
        IEnumerable<AppCapability> capabilities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(capabilities);

        Id = id;
        CustomName = string.IsNullOrWhiteSpace(customName) ? null : customName.Trim();
        NameKey = nameKey;
        IsBuiltIn = isBuiltIn;
        Capabilities = new HashSet<AppCapability>(capabilities);
    }

    /// <summary>Stable identifier. What <c>ShopMembership.RoleIds</c> stores; never renamed.</summary>
    public string Id { get; }

    /// <summary>The name an administrator typed, or null for a shipped role named by the string table.</summary>
    public string? CustomName { get; }

    /// <summary>String-table key naming a shipped role, or null for one that carries its own name.</summary>
    public string? NameKey { get; }

    /// <summary>Shipped with the application: editable, but it cannot be deleted.</summary>
    public bool IsBuiltIn { get; }

    /// <summary>True for the one role that is an account flag rather than a shop membership.</summary>
    public bool IsAdministratorRole => string.Equals(Id, AdministratorId, StringComparison.Ordinal);

    /// <summary>Whether this role's capabilities may be changed at all.</summary>
    public bool IsLocked => IsAdministratorRole;

    /// <summary>Whether this role can be given to somebody in a shop.</summary>
    /// <remarks>
    /// The administrator cannot: it is held by the account, everywhere at once, so recording it
    /// against one shop would be a second and contradictable source of the same answer.
    /// </remarks>
    public bool IsAssignable => !IsAdministratorRole;

    /// <summary>What this role may do.</summary>
    public IReadOnlySet<AppCapability> Capabilities { get; }

    /// <summary>Whether this role carries a capability.</summary>
    public bool Grants(AppCapability capability) => Capabilities.Contains(capability);

    /// <summary>
    /// What to call this role on screen: the string table for a shipped one, so it stays translated
    /// into languages added later, and the typed name for a role somebody created.
    /// </summary>
    public string ResolveName(ILocalizedText text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (!string.IsNullOrWhiteSpace(CustomName))
            return CustomName;

        return NameKey is null ? Id : text[NameKey];
    }

    /// <summary>The same role with a different capability set.</summary>
    public RoleDefinition WithCapabilities(IEnumerable<AppCapability> capabilities)
        => new(Id, CustomName, NameKey, IsBuiltIn, capabilities);

    /// <summary>The same role under a new display name. Built-ins lose their string-table key when
    /// renamed — an administrator who types a name has overruled the shipped one.</summary>
    public RoleDefinition WithName(string? customName)
        => new(Id, customName, string.IsNullOrWhiteSpace(customName) ? NameKey : null,
            IsBuiltIn, Capabilities);
}

/// <summary>
/// The roles a fresh installation has, and what they may do.
/// </summary>
/// <remarks>
/// THE DEFAULTS DELIBERATELY REPRODUCE THE OLD HARD-CODED RULES EXACTLY, including the ones that
/// look generous. Staff keep the settlement report because the Settlement menu was never gated, and
/// an upgrade that quietly took a screen away from every shop assistant in the country would be a
/// permissions release that starts by breaking permissions. Trimming it is one tick box away, which
/// is the entire point of the panel.
/// </remarks>
public static class BuiltInRoles
{
    /// <summary>Everything a role can be given — the manager's set, minus the shop configuration.</summary>
    private static readonly AppCapability[] OrderWork =
    {
        AppCapability.ViewOrders,
        AppCapability.CreateOrders,
        AppCapability.EditOrders,
        AppCapability.DeleteOrders,
        AppCapability.CopyOrders,
        AppCapability.PrintOrderDocuments,
        AppCapability.ViewReports,
        AppCapability.ExportReports
    };

    /// <summary>
    /// Order work a MANAGER gets and a staff member does not (v8.0).
    /// </summary>
    /// <remarks>
    /// Both are destructive or disclosing in a way ordinary order work is not: the recycle bin is
    /// where a record is removed beyond recovery and where every order anybody has deleted can be
    /// read, and the export walks out of the building with the whole customer list in one file.
    /// Deleting an order, by contrast, is now reversible and so costs less than it used to.
    ///
    /// This only decides what a FRESH installation starts with. An upgraded one keeps whatever its
    /// roles already stored — <c>TopUpBuiltIns</c> restores missing ROLES, never missing capabilities
    /// — because a role's capability set is the shop's own statement and an upgrade that quietly
    /// widened it would be a permissions release that starts by overruling permissions.
    /// </remarks>
    private static readonly AppCapability[] SeniorOrderWork =
    {
        AppCapability.ManageRecycleBin,
        AppCapability.ExportOrders
    };

    private static readonly AppCapability[] ShopConfiguration =
    {
        AppCapability.ConfigureShop,
        AppCapability.ManageMeasurementTerms,
        AppCapability.ManageProductCatalog,
        AppCapability.ManageBranding,
        AppCapability.ManageStoreMembers
    };

    /// <summary>The administrator: every capability there is, including the three no role may hold.</summary>
    public static RoleDefinition Administrator() => new(
        RoleDefinition.AdministratorId, customName: null, nameKey: "Shop.Role.Admin",
        isBuiltIn: true, Enum.GetValues<AppCapability>());

    /// <summary>Runs one shop: its orders, its reports and its configuration.</summary>
    /// <remarks>
    /// Not <c>ManageBackups</c>. A restore replaces EVERY shop's data at once, so on a multi-branch
    /// installation one manager putting their own branch back would take the others with it. It is
    /// grantable — a single-shop installation where the manager is the owner will want to grant it —
    /// but it is not something to hand out by default.
    /// </remarks>
    public static RoleDefinition Manager() => new(
        RoleDefinition.ManagerId, customName: null, nameKey: "Shop.Role.Manager",
        isBuiltIn: true, OrderWork.Concat(SeniorOrderWork).Concat(ShopConfiguration));

    /// <summary>Takes orders in one shop, with no access to how it is configured.</summary>
    public static RoleDefinition Staff() => new(
        RoleDefinition.StaffId, customName: null, nameKey: "Shop.Role.Staff",
        isBuiltIn: true, OrderWork);

    /// <summary>
    /// Reads the books and touches nothing. Seeded ONCE rather than shipped built-in, so an
    /// installation that does not want it can delete it and have that stick.
    /// </summary>
    public static RoleDefinition Auditor() => new(
        RoleDefinition.AuditorId, customName: null, nameKey: "Shop.Role.Auditor",
        isBuiltIn: false,
        new[] { AppCapability.ViewOrders, AppCapability.ViewReports, AppCapability.ExportReports });

    /// <summary>The roles that must always exist, in display order.</summary>
    public static IReadOnlyList<RoleDefinition> All() => new[] { Administrator(), Manager(), Staff() };
}
