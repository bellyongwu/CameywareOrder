using System.IO;
using System.Text.Json;
using CameywareOrder.Configuration;
using CameywareOrder.Localization;
using CameywareOrder.Models;
using Path = System.IO.Path;

namespace CameywareOrder.Services;

/// <summary>
/// A shop's ready-made product catalogue: the categories its order editor offers, and how each one
/// is named in each language.
/// </summary>
/// <remarks>
/// Modelled on <see cref="MeasurementTermsService"/> and stored the same way — one JSON file per
/// shop, keyed on <c>Shop.PublicId</c> — because it answers the same kind of question: a per-branch
/// list that starts from a shipped default and is then the shop's to edit.
///
/// The categories used to be a <c>static readonly string[]</c> in the order editor, so every shop in
/// every installation sold exactly the same five things and adding a sixth meant a rebuild.
/// </remarks>
public sealed class ProductCatalogService
{
    public static ProductCatalogService Instance { get; } = new();

    // Keyed on PublicId, NEVER Id: ids are local autoincrement values and whole databases move
    // between machines, so an imported shop would otherwise pick up an unrelated shop's catalogue.
    private static string ShopFileName(Shop shop) => $"product-catalog-{shop.PublicId:N}.json";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly ProductCatalogConfig _config;
    private Shop? _shop;

    private ProductCatalogService() => _config = ProductCatalogDefaults.CreateDefaultConfig();

    public event EventHandler? ConfigChanged;

    /// <summary>The bound shop's categories, in the order the editor should offer them.</summary>
    public IReadOnlyList<ProductItem> Items => _config.Items;

    /// <summary>
    /// Points the service at a shop and loads that shop's catalogue in place. A shop with no file
    /// yet starts from the shipped defaults, which are then written for it — so "load default
    /// settings, keep as what we had" is simply what a shop does before anyone edits anything.
    /// </summary>
    public void BindTo(Shop shop)
    {
        ArgumentNullException.ThrowIfNull(shop);

        _shop = shop;

        var loaded = TryLoad(SettingFilePath) ?? ProductCatalogDefaults.CreateDefaultConfig();
        ReplaceConfigInPlace(loaded);
        Persist();
        ConfigChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Display name of a category in a language: a user-added one's own name, a predefined one's
    /// translation from the string table, and — for an id in neither — the id itself.
    /// </summary>
    /// <remarks>
    /// The last case is not defensive padding. Orders saved before this catalogue existed hold a
    /// raw category id in <c>OrderItem.ProductName</c>, and a shop is free to delete a category it
    /// no longer sells; either way the historical order must still print something a person can
    /// read rather than a blank.
    /// </remarks>
    public string ResolveName(string? id, string languageCode)
    {
        if (string.IsNullOrWhiteSpace(id))
            return string.Empty;

        var item = _config.Items.Find(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal));

        if (item is not null && !item.IsPredefined)
        {
            if (item.Names.TryGetValue(languageCode, out var exact) && !string.IsNullOrWhiteSpace(exact))
                return exact;

            // Any language that HAS a name beats showing the raw id — the same fallback the shop
            // name and address use.
            var any = item.Names.Values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (!string.IsNullOrWhiteSpace(any))
                return any;
        }

        var key = ProductCatalogDefaults.NameKey(id);
        var localized = LocalizationService.Instance.GetText(key, languageCode);

        // GetText returns the key itself when it is not in the table.
        return string.Equals(localized, key, StringComparison.Ordinal) ? id : localized;
    }

    /// <summary>Display name in the language the UI is currently running in.</summary>
    public string ResolveName(string? id) => ResolveName(id, LocalizationService.Instance.CurrentLanguageCode);

    /// <summary>Adds a category. Returns false when the id is already taken.</summary>
    public bool Add(ProductItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (_config.Items.Exists(existing => string.Equals(existing.Id, item.Id, StringComparison.Ordinal)))
            return false;

        _config.Items.Add(item);
        Persist();
        ConfigChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Replaces a user-added category's per-language names. Predefined ones are locked.</summary>
    public bool Rename(string id, IReadOnlyDictionary<string, string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        var item = _config.Items.Find(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal));
        if (item is null || item.IsPredefined)
            return false;

        item.Names = new Dictionary<string, string>(names);
        Persist();
        ConfigChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Removes a category from this shop's catalogue. Allowed for predefined ones too — a shop that
    /// does not sell shoes should not have to offer "Leather Shoes" — and safe, because orders store
    /// the id and <see cref="ResolveName"/> still resolves it afterwards.
    /// </summary>
    public bool Remove(string id)
    {
        var removed = _config.Items.RemoveAll(item => string.Equals(item.Id, id, StringComparison.Ordinal)) > 0;
        if (!removed)
            return false;

        Persist();
        ConfigChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Moves a category one place up or down, so the editor can offer the common ones first.</summary>
    public bool Move(string id, int offset)
    {
        var index = _config.Items.FindIndex(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        var target = index + offset;

        if (index < 0 || target < 0 || target >= _config.Items.Count)
            return false;

        (_config.Items[index], _config.Items[target]) = (_config.Items[target], _config.Items[index]);
        Persist();
        ConfigChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Puts the catalogue back to the shipped categories. Discards user-added ones, which is why the
    /// caller confirms first — the same contract as restoring a garment's default measurements.
    /// </summary>
    public void RestoreDefaults()
    {
        ReplaceConfigInPlace(ProductCatalogDefaults.CreateDefaultConfig());
        Persist();
        ConfigChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Seeds <paramref name="target"/>'s catalogue from <paramref name="source"/>'s — the new-shop
    /// wizard's "copy from an existing shop". Copies the FILE, so the source need not be the open
    /// shop and the active binding is untouched.
    /// </summary>
    public static void CopyConfigBetweenShops(Shop source, Shop target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        try
        {
            var from = Path.Combine(SettingDirectory, ShopFileName(source));
            var to = Path.Combine(SettingDirectory, ShopFileName(target));

            // Never overwrite: a shop that already has a catalogue has been configured.
            if (!File.Exists(from) || File.Exists(to))
                return;

            Directory.CreateDirectory(SettingDirectory);
            File.Copy(from, to);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort, like every other persistence path here: the new shop falls back to the
            // shipped defaults rather than the creation failing.
        }
    }

    private void ReplaceConfigInPlace(ProductCatalogConfig source)
    {
        _config.Items.Clear();
        _config.Items.AddRange(source.Items);
    }

    private static string SettingDirectory => UserDataPaths.ShopDataDirectory;

    private string SettingFilePath => _shop is null
        ? Path.Combine(SettingDirectory, "product-catalog.json")
        : Path.Combine(SettingDirectory, ShopFileName(_shop));

    private static ProductCatalogConfig? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            var config = JsonSerializer.Deserialize<ProductCatalogConfig>(File.ReadAllText(path));

            // An empty catalogue would leave the order editor with nothing to choose, which reads as
            // a broken screen rather than as a configuration choice. Treat it as "not configured".
            return config is null || config.Items.Count == 0 ? null : config;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private void Persist()
    {
        try
        {
            Directory.CreateDirectory(SettingDirectory);
            File.WriteAllText(SettingFilePath, JsonSerializer.Serialize(_config, WriteOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Non-fatal: the in-memory catalogue keeps working for this session.
        }
    }
}
