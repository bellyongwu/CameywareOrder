using System.IO;
using System.Text.Json;
using CameywareOrder.Models;
using Path = System.IO.Path;

namespace CameywareOrder.Services;

/// <summary>Horizontal placement of the logo in printed receipts and the measurements PDF.</summary>
public enum LogoPlacement
{
    Left,
    Center,
    Right
}

/// <summary>
/// Rich header/footer content for a single language. The rich text is stored as a
/// serialized WPF FlowDocument XAML string so it round-trips losslessly into the
/// printed receipt (native FlowDocument) and can be walked for the QuestPDF export.
/// </summary>
public sealed class LocalizedBranding
{
    public string? HeaderXaml { get; set; }
    public string? FooterXaml { get; set; }
}

/// <summary>
/// Persisted receipt / document branding: an optional shared logo image plus
/// per-language rich header and footer content.
/// </summary>
public sealed class ReceiptBrandingSettings
{
    public string? LogoFileName { get; set; }

    public LogoPlacement LogoPlacement { get; set; } = LogoPlacement.Center;

    public Dictionary<string, LocalizedBranding> Languages { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public LocalizedBranding ForLanguage(string languageCode)
    {
        if (!Languages.TryGetValue(languageCode, out var branding))
        {
            branding = new LocalizedBranding();
            Languages[languageCode] = branding;
        }

        return branding;
    }
}

/// <summary>
/// Loads and saves <see cref="ReceiptBrandingSettings"/> as JSON under the app's
/// local AppData folder, and manages the logo image file next to it. Mirrors the
/// resilient, non-fatal persistence style used by the language preference store.
/// </summary>
public static class ReceiptBrandingStore
{
    private const string FileName = "receipt-branding.json";

    private static string BrandingRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CameywareOrder",
            "Branding");

    /// <summary>
    /// Branding folder for the open shop, or the shared root before a shop is open — which is also
    /// where the pre-multi-shop branding already lives, so the first shop inherits it unchanged.
    ///
    /// Sub-folders are keyed on <see cref="Shop.PublicId"/>, NEVER <see cref="Shop.Id"/>: ids are
    /// local autoincrement values and whole databases move between machines, so an imported shop
    /// would otherwise adopt an unrelated local shop's logo and header.
    ///
    /// This class stays static and stateless — it re-reads on every Load() — so it deliberately
    /// asks ShopContext for the shop each time rather than caching one.
    /// </summary>
    private static string BrandingDirectory
    {
        get
        {
            var shop = ShopContext.Instance.Current;
            return shop is null ? BrandingRoot : Path.Combine(BrandingRoot, shop.PublicId.ToString("N"));
        }
    }

    private static string SettingsFilePath => Path.Combine(BrandingDirectory, FileName);

    /// <summary>
    /// One-time migration: gives the first shop the header, footer and logo this machine already
    /// had. Copies the files sitting directly in the shared Branding root into the shop's own
    /// folder — top level only, so other shops' folders are never swept in — and leaves the
    /// originals as a rollback safety net.
    /// </summary>
    public static void AdoptLegacyFolderFor(Shop shop)
    {
        ArgumentNullException.ThrowIfNull(shop);

        try
        {
            var target = Path.Combine(BrandingRoot, shop.PublicId.ToString("N"));
            if (Directory.Exists(target) || !Directory.Exists(BrandingRoot))
                return;

            Directory.CreateDirectory(target);
            foreach (var file in Directory.EnumerateFiles(BrandingRoot))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        }
        catch (IOException)
        {
            // Best-effort: a shop with no branding simply prints without a header.
        }
    }

    public static ReceiptBrandingSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
                return new ReceiptBrandingSettings();

            var json = File.ReadAllText(SettingsFilePath);
            return JsonSerializer.Deserialize<ReceiptBrandingSettings>(json) ?? new ReceiptBrandingSettings();
        }
        catch
        {
            return new ReceiptBrandingSettings();
        }
    }

    public static void Save(ReceiptBrandingSettings settings)
    {
        Directory.CreateDirectory(BrandingDirectory);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsFilePath, json);
    }

    /// <summary>Absolute path to the stored logo, or null when none is set / missing.</summary>
    public static string? GetLogoPath(ReceiptBrandingSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.LogoFileName))
            return null;

        var path = Path.Combine(BrandingDirectory, settings.LogoFileName);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Reads the stored logo bytes, or null when none is available.</summary>
    public static byte[]? GetLogoBytes(ReceiptBrandingSettings settings)
    {
        var path = GetLogoPath(settings);
        if (path is null)
            return null;

        try
        {
            return File.ReadAllBytes(path);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Copies the chosen image into the branding folder (replacing any previous
    /// logo) and returns the stored file name. The caller assigns it to
    /// <see cref="ReceiptBrandingSettings.LogoFileName"/> and persists via <see cref="Save"/>.
    /// </summary>
    public static string ImportLogo(string sourcePath)
    {
        Directory.CreateDirectory(BrandingDirectory);
        RemoveExistingLogoFiles();

        var extension = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(extension))
            extension = ".png";

        var fileName = $"logo{extension}";
        File.Copy(sourcePath, Path.Combine(BrandingDirectory, fileName), overwrite: true);
        return fileName;
    }

    public static void RemoveLogo(ReceiptBrandingSettings settings)
    {
        RemoveExistingLogoFiles();
        settings.LogoFileName = null;
    }

    private static void RemoveExistingLogoFiles()
    {
        try
        {
            if (!Directory.Exists(BrandingDirectory))
                return;

            foreach (var file in Directory.EnumerateFiles(BrandingDirectory, "logo.*"))
                File.Delete(file);
        }
        catch
        {
            // Non-fatal: a stale logo file left behind is harmless.
        }
    }

    /// <summary>
    /// Builds the self-contained export payload: the current settings plus the logo image
    /// bytes as base64, so an export/import round-trip restores the logo too. Shared by the
    /// standalone branding export and the global-settings package, which embeds the object
    /// directly rather than nesting a JSON string inside its own JSON.
    /// </summary>
    public static BrandingExport BuildExport()
    {
        var settings = Load();
        var logoBytes = GetLogoBytes(settings);
        return new BrandingExport
        {
            Settings = settings,
            LogoFileName = logoBytes is not null ? settings.LogoFileName : null,
            LogoBase64 = logoBytes is not null ? Convert.ToBase64String(logoBytes) : null
        };
    }

    /// <summary>Serializes <see cref="BuildExport"/> for the standalone branding export file.</summary>
    public static string ExportConfigJson()
        => JsonSerializer.Serialize(BuildExport(), new JsonSerializerOptions { WriteIndented = true });

    /// <summary>Parses an export document, returning null when the JSON is invalid/corrupt.</summary>
    public static BrandingExport? TryParseConfigJson(string json)
    {
        try
        {
            var export = JsonSerializer.Deserialize<BrandingExport>(json);
            return export?.Settings is null ? null : export;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Replaces the persisted settings and logo image with the imported ones.</summary>
    public static void ImportConfig(BrandingExport export)
    {
        Directory.CreateDirectory(BrandingDirectory);
        RemoveExistingLogoFiles();

        var settings = export.Settings ?? new ReceiptBrandingSettings();

        if (!string.IsNullOrWhiteSpace(export.LogoBase64) && !string.IsNullOrWhiteSpace(export.LogoFileName))
        {
            var bytes = Convert.FromBase64String(export.LogoBase64);
            File.WriteAllBytes(Path.Combine(BrandingDirectory, export.LogoFileName), bytes);
            settings.LogoFileName = export.LogoFileName;
        }
        else
        {
            settings.LogoFileName = null;
        }

        Save(settings);
    }
}

/// <summary>Self-contained export/import payload for <see cref="ReceiptBrandingSettings"/>.</summary>
public sealed class BrandingExport
{
    public ReceiptBrandingSettings? Settings { get; set; }
    public string? LogoFileName { get; set; }
    public string? LogoBase64 { get; set; }
}
