namespace CameywareOrder.Models;

/// <summary>
/// One ready-made product category a shop sells (Jackets, Ties / Bowtie, …), as offered in the
/// order editor's clothing rows.
/// </summary>
/// <remarks>
/// Deliberately the same shape as <see cref="GarmentType"/>: a stable <see cref="Id"/>, a locked
/// flag, and per-language <see cref="Names"/> used only by user-added entries. The predefined ones
/// keep the ids the application has always written into <c>OrderItem.ProductName</c> — "Jackets",
/// "TiesBowtie", … — so existing orders keep resolving to the same string-table entries and nothing
/// already saved has to be migrated.
/// </remarks>
public class ProductItem
{
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// True for the shipped categories, whose names come from the string table
    /// (<c>ClothingItem.&lt;Id&gt;</c>) and are therefore translated for every language the
    /// application ships — including ones added after the shop was configured. A predefined entry
    /// can be REMOVED from a shop's catalogue but not renamed, because its name is not the shop's
    /// to own.
    /// </summary>
    public bool IsPredefined { get; set; }

    /// <summary>Language code → display name. Only used by user-added categories.</summary>
    public Dictionary<string, string> Names { get; set; } = new();
}

/// <summary>
/// A shop's ready-made product catalogue: which categories its order editor offers, in order.
/// </summary>
/// <remarks>
/// Per shop, like measurement terms and branding — a branch that sells shoes and one that sells only
/// suits should not be forced to share a list. Serialized to JSON under the app's local data folder.
/// </remarks>
public class ProductCatalogConfig
{
    public List<ProductItem> Items { get; set; } = new();
}

/// <summary>
/// The categories a shop starts with, and the ids the application has always used for them.
/// </summary>
/// <remarks>
/// These ids are a COMPATIBILITY SURFACE, not an implementation detail: every order ever saved
/// stores one of them in <c>OrderItem.ProductName</c>, and every language file has a matching
/// <c>ClothingItem.&lt;id&gt;</c> entry. Renaming one here silently orphans historical orders, which
/// would then print their raw id instead of a product name. Add new ones freely; do not rename.
/// </remarks>
public static class ProductCatalogDefaults
{
    /// <summary>Ordered ids of the shipped categories.</summary>
    public static readonly IReadOnlyList<string> PredefinedIds = new[]
    {
        "Jackets",
        "TiesBowtie",
        "Qipao",
        "LeatherShoes",
        "Other"
    };

    /// <summary>String-table key holding a predefined category's translated name.</summary>
    public static string NameKey(string id) => $"ClothingItem.{id}";

    /// <summary>The catalogue a shop starts with: the shipped categories, in their shipped order.</summary>
    public static ProductCatalogConfig CreateDefaultConfig()
    {
        var config = new ProductCatalogConfig();

        foreach (var id in PredefinedIds)
            config.Items.Add(new ProductItem { Id = id, IsPredefined = true });

        return config;
    }

    /// <summary>Whether an id is one of the shipped categories.</summary>
    public static bool IsPredefinedId(string? id)
        => id is not null && PredefinedIds.Contains(id, StringComparer.Ordinal);
}
