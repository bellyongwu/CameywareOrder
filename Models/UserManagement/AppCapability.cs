namespace CameywareOrder.Models;

/// <summary>
/// One thing a user may be allowed to do. The whole application's permission surface, as data.
///
/// Until this existed, "what may this user do" was answered by comparing their role to a constant —
/// <c>IsAdministrator || CurrentRole == UserRole.Manager</c> — in a named property per question. That
/// is a fixed set of answers baked into the build: an installation that wanted a role which reads the
/// settlement report and touches nothing else had no way to say so.
///
/// Now a role is a SET of these values, stored as data, and every gate in the UI asks
/// <c>AuthenticationService.Can(...)</c>. Adding a capability means adding a value here, a
/// <see cref="CapabilityCatalog"/> entry, its two string-table keys, and the one gate that reads it.
///
/// THE NAMES ARE A COMPATIBILITY SURFACE. They are written into <c>roles.json</c> as text, so
/// renaming a value silently strips that permission from every role that held it. Add and deprecate;
/// do not rename.
/// </summary>
public enum AppCapability
{
    // --- Orders ---------------------------------------------------------------------------------

    /// <summary>See the order list and the detail panel at all. Without it the shop opens empty.</summary>
    ViewOrders,

    /// <summary>Start a new order.</summary>
    CreateOrders,

    /// <summary>Change a saved order. Without it the editor still opens, read-only.</summary>
    EditOrders,

    /// <summary>Delete an order, singly or as a batch.</summary>
    DeleteOrders,

    /// <summary>Duplicate an existing order into a new one.</summary>
    CopyOrders,

    /// <summary>Print or download a receipt and a measurement sheet.</summary>
    PrintOrderDocuments,

    // --- Reporting ------------------------------------------------------------------------------

    /// <summary>Open the settlement report, and see the summary figures on the main window.</summary>
    ViewReports,

    /// <summary>Print the settlement report or save it as a PDF.</summary>
    ExportReports,

    // --- Shop configuration ---------------------------------------------------------------------

    /// <summary>Change the open shop's own settings: its details, currency, tax and numbering.</summary>
    ConfigureShop,

    /// <summary>Edit the open shop's measurement terms.</summary>
    ManageMeasurementTerms,

    /// <summary>Edit the open shop's product categories.</summary>
    ManageProductCatalog,

    /// <summary>Edit the receipt letterhead — header, footer and logo.</summary>
    ManageBranding,

    /// <summary>Manage who works in the open shop: their role, shift and whether they are active.</summary>
    ManageStoreMembers,

    // --- The installation -----------------------------------------------------------------------

    /// <summary>Create a shop, and reach the store-management tools.</summary>
    CreateShops,

    /// <summary>The Local Database and Import / Export menus, which act on the whole installation.</summary>
    UseDataTools,

    /// <summary>Run the application in any shipped language, not only the ones the shop installs.</summary>
    ChooseAnyLanguage,

    /// <summary>Manage every account in the installation. Administrators only — see the catalog.</summary>
    ManageUsers,

    /// <summary>Delete an account outright. Administrators only — see the catalog.</summary>
    DeleteAccounts,

    /// <summary>Define roles and what they may do. Administrators only — see the catalog.</summary>
    ManagePermissions
}

/// <summary>Which grouping a capability is shown under, and which module of the app it governs.</summary>
public enum CapabilityGroup
{
    Orders,
    Reporting,
    ShopConfiguration,
    People,
    Installation
}

/// <summary>
/// Whether a capability is asked about ONE shop or about the installation as a whole.
/// </summary>
/// <remarks>
/// This is not cosmetic — it decides which memberships answer the question. A shop-scoped capability
/// is resolved against the shop currently open, so the same person can edit orders in one branch and
/// only read them in another. An installation-scoped one is resolved across EVERY active membership,
/// because the screens that ask it — the shop picker above all — run with no shop open at all, and a
/// question that can only be answered "no" there is a capability that can never be granted.
/// </remarks>
public enum CapabilityScope
{
    Shop,
    Installation
}

/// <summary>One capability and everything the UI needs to show and resolve it.</summary>
/// <param name="Capability">The value itself.</param>
/// <param name="Group">Where it appears in the permission tree.</param>
/// <param name="Scope">Which memberships answer it — see <see cref="CapabilityScope"/>.</param>
/// <param name="AdministratorOnly">
/// True for the three capabilities that cannot be delegated to a role at all. They are still LISTED,
/// shown held-and-locked, because an administrator asking "who can delete an account" deserves the
/// answer rather than a gap where the question should be.
/// </param>
public readonly record struct CapabilityEntry(
    AppCapability Capability,
    CapabilityGroup Group,
    CapabilityScope Scope,
    bool AdministratorOnly)
{
    /// <summary>String-table key naming this capability.</summary>
    public string NameKey => $"Permission.Capability.{Capability}";

    /// <summary>String-table key for the one line explaining what granting it actually allows.</summary>
    public string DescriptionKey => $"Permission.Capability.{Capability}.Detail";
}

/// <summary>
/// Every capability the application has, in the order the permission panel shows them.
/// </summary>
/// <remarks>
/// THE LIST IS THE PRODUCT'S DEFINITION OF ITSELF. A feature that is gated but missing here cannot be
/// granted to anyone, and a feature listed here but gated nowhere is a promise the application does
/// not keep — <c>permcheck</c> asserts both directions against the source rather than against this
/// list, so neither can drift quietly.
/// </remarks>
public static class CapabilityCatalog
{
    private static readonly CapabilityEntry[] Entries =
    {
        Shop(AppCapability.ViewOrders, CapabilityGroup.Orders),
        Shop(AppCapability.CreateOrders, CapabilityGroup.Orders),
        Shop(AppCapability.EditOrders, CapabilityGroup.Orders),
        Shop(AppCapability.DeleteOrders, CapabilityGroup.Orders),
        Shop(AppCapability.CopyOrders, CapabilityGroup.Orders),
        Shop(AppCapability.PrintOrderDocuments, CapabilityGroup.Orders),

        Shop(AppCapability.ViewReports, CapabilityGroup.Reporting),
        Shop(AppCapability.ExportReports, CapabilityGroup.Reporting),

        Shop(AppCapability.ConfigureShop, CapabilityGroup.ShopConfiguration),
        Shop(AppCapability.ManageMeasurementTerms, CapabilityGroup.ShopConfiguration),
        Shop(AppCapability.ManageProductCatalog, CapabilityGroup.ShopConfiguration),
        Shop(AppCapability.ManageBranding, CapabilityGroup.ShopConfiguration),

        Shop(AppCapability.ManageStoreMembers, CapabilityGroup.People),
        Locked(AppCapability.ManageUsers, CapabilityGroup.People),
        Locked(AppCapability.DeleteAccounts, CapabilityGroup.People),
        Locked(AppCapability.ManagePermissions, CapabilityGroup.People),

        Installation(AppCapability.CreateShops),
        Installation(AppCapability.UseDataTools),
        Installation(AppCapability.ChooseAnyLanguage)
    };

    private static readonly Dictionary<AppCapability, CapabilityEntry> ByCapability =
        Entries.ToDictionary(entry => entry.Capability);

    /// <summary>Every capability, in display order.</summary>
    public static IReadOnlyList<CapabilityEntry> All => Entries;

    /// <summary>The groups that actually carry entries, in display order.</summary>
    public static IReadOnlyList<CapabilityGroup> Groups { get; } =
        Entries.Select(entry => entry.Group).Distinct().ToArray();

    /// <summary>The capabilities in one group, in display order.</summary>
    public static IReadOnlyList<CapabilityEntry> InGroup(CapabilityGroup group)
        => Entries.Where(entry => entry.Group == group).ToArray();

    /// <summary>
    /// The entry for a capability. Every value of the enum has one — a missing entry is a
    /// programming error rather than a runtime condition, so this throws rather than returning null.
    /// </summary>
    public static CapabilityEntry Entry(AppCapability capability) => ByCapability[capability];

    /// <summary>Whether a capability may be given to a role at all, or is the administrator's alone.</summary>
    public static bool IsGrantable(AppCapability capability) => !Entry(capability).AdministratorOnly;

    /// <summary>Every capability a role is allowed to be given.</summary>
    public static IReadOnlyList<AppCapability> Grantable { get; } =
        Entries.Where(entry => !entry.AdministratorOnly).Select(entry => entry.Capability).ToArray();

    /// <summary>String-table key naming a group.</summary>
    public static string GroupNameKey(CapabilityGroup group) => $"Permission.Group.{group}";

    private static CapabilityEntry Shop(AppCapability capability, CapabilityGroup group)
        => new(capability, group, CapabilityScope.Shop, AdministratorOnly: false);

    private static CapabilityEntry Installation(AppCapability capability)
        => new(capability, CapabilityGroup.Installation, CapabilityScope.Installation,
            AdministratorOnly: false);

    // The keys to the installation itself. A role that could grant capabilities could grant itself
    // every capability, so delegating these would make every other restriction advisory.
    private static CapabilityEntry Locked(AppCapability capability, CapabilityGroup group)
        => new(capability, group, CapabilityScope.Installation, AdministratorOnly: true);
}
