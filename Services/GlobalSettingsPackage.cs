using System.IO;
using System.IO.Compression;
using System.Text.Json;
using LeeYongeOrdering.Data;
using LeeYongeOrdering.Localization;
using LeeYongeOrdering.Models;
using Path = System.IO.Path;

namespace LeeYongeOrdering.Services;

/// <summary>
/// Everything the app keeps on this machine, in one file: the order database (with its
/// attached document images), the measurement-terms configuration, the receipt branding
/// (header/footer plus the logo), the currency choice and the UI language.
///
/// The package is a zip holding <c>settings.json</c> for the small settings and a nested
/// <c>database.zip</c> produced by <see cref="DatabasePathProvider.ExportDatabaseTo"/>.
/// Nesting the database package rather than re-implementing it keeps one code path for the
/// db + WAL/SHM sidecars + <c>Documents/</c> tree, and means the database import keeps its
/// existing backup and zip-slip handling.
///
/// Self-contained by design (see the Import/Export rule in the skill notes): the logo travels
/// as base64 inside the branding payload and every document image inside the nested package,
/// so restoring on another PC leaves nothing dangling.
/// </summary>
public static class GlobalSettingsPackage
{
    private const string SettingsEntryName = "settings.json";
    private const string DatabaseEntryName = "database.zip";

    /// <summary>Bumped only if the payload shape changes incompatibly; import accepts anything it can read.</summary>
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public static void ExportTo(string targetPath)
    {
        var payload = new GlobalSettingsExport
        {
            Version = CurrentVersion,
            ExportedAt = DateTime.Now.ToString("O"),
            Currency = CurrencySettingService.Instance.Current,
            LanguageCode = LocalizationService.Instance.CurrentLanguageCode,
            MeasurementTerms = MeasurementTermsService.Instance.Config,
            Branding = ReceiptBrandingStore.BuildExport()
        };

        // The database package is built to a temporary file first so its existing
        // single-responsibility export stays untouched.
        var temporaryDatabasePath = Path.Combine(Path.GetTempPath(), $"leeyonge-db-{Guid.NewGuid():N}.zip");
        try
        {
            DatabasePathProvider.ExportDatabaseTo(temporaryDatabasePath);

            if (File.Exists(targetPath))
                File.Delete(targetPath);

            using var archive = ZipFile.Open(targetPath, ZipArchiveMode.Create);

            var settingsEntry = archive.CreateEntry(SettingsEntryName);
            using (var writer = new StreamWriter(settingsEntry.Open()))
                writer.Write(JsonSerializer.Serialize(payload, SerializerOptions));

            archive.CreateEntryFromFile(temporaryDatabasePath, DatabaseEntryName);
        }
        finally
        {
            DeleteIfExists(temporaryDatabasePath);
        }
    }

    /// <summary>
    /// Reads and validates a package without changing anything, so the caller can confirm with
    /// the user before overwriting. Returns null when the file is not a readable global-settings
    /// package.
    /// </summary>
    public static GlobalSettingsExport? TryRead(string sourcePath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(sourcePath);

            var settingsEntry = archive.GetEntry(SettingsEntryName);
            if (settingsEntry is null)
                return null;

            using var reader = new StreamReader(settingsEntry.Open());
            var payload = JsonSerializer.Deserialize<GlobalSettingsExport>(reader.ReadToEnd());
            if (payload is null)
                return null;

            payload.ContainsDatabase = archive.GetEntry(DatabaseEntryName) is not null;
            return payload;
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Anything unreadable is reported as "not a valid package" rather than thrown:
            // the caller uses this to decide whether to even offer the destructive confirm.
            return null;
        }
    }

    /// <summary>
    /// Restores every setting the package carries. Sections the package omits are left alone,
    /// so an older or partial package never blanks out settings it knows nothing about. The
    /// database is restored first because it is the only destructive step and the only one
    /// that takes its own backup.
    /// </summary>
    public static void Import(string sourcePath, GlobalSettingsExport payload)
    {
        RestoreDatabase(sourcePath);

        if (payload.MeasurementTerms is not null)
            MeasurementTermsService.Instance.ImportConfig(payload.MeasurementTerms);

        if (payload.Branding is not null)
            ReceiptBrandingStore.ImportConfig(payload.Branding);

        if (payload.Currency is not null)
            CurrencySettingService.Instance.SetCurrency(payload.Currency.Value);

        RestoreLanguage(payload.LanguageCode);
    }

    private static void RestoreDatabase(string sourcePath)
    {
        using var archive = ZipFile.OpenRead(sourcePath);
        var databaseEntry = archive.GetEntry(DatabaseEntryName);
        if (databaseEntry is null)
            return;

        var temporaryDatabasePath = Path.Combine(Path.GetTempPath(), $"leeyonge-db-{Guid.NewGuid():N}.zip");
        try
        {
            databaseEntry.ExtractToFile(temporaryDatabasePath, overwrite: true);
            DatabasePathProvider.ImportDatabaseFrom(temporaryDatabasePath);
        }
        finally
        {
            DeleteIfExists(temporaryDatabasePath);
        }
    }

    private static void RestoreLanguage(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return;

        // SetLanguage returns false for a code this build does not ship; leaving the current
        // language alone is better than failing the whole restore over it.
        if (LocalizationService.Instance.SetLanguage(languageCode))
            new LanguagePreferenceStore().SaveLanguageCode(languageCode);
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // A leftover file in the temp folder is not worth failing the operation for.
        }
    }
}

/// <summary>Payload of <c>settings.json</c> inside a global-settings package.</summary>
public sealed class GlobalSettingsExport
{
    public int Version { get; set; } = GlobalSettingsPackage.CurrentVersion;

    public string? ExportedAt { get; set; }

    public CurrencyType? Currency { get; set; }

    public string? LanguageCode { get; set; }

    public MeasurementTermsConfig? MeasurementTerms { get; set; }

    public BrandingExport? Branding { get; set; }

    /// <summary>Set while reading a package; not part of the serialized payload.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool ContainsDatabase { get; set; }
}
