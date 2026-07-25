using System.IO;
using System.Text.Json;
using Path = System.IO.Path;

namespace LeeYongeOrdering.Services;

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

    private static string BrandingDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LeeYongeOrdering",
            "Branding");

    private static string SettingsFilePath => Path.Combine(BrandingDirectory, FileName);

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
}
