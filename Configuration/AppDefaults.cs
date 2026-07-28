using System.IO;
using System.Text.Json;

namespace CameywareOrder.Configuration;

/// <summary>
/// Defaults read from <c>Settings/System/Defaults/app-defaults.json</c>.
/// </summary>
/// <remarks>
/// Every member has a usable value even when the file is missing or malformed. This is read during
/// startup before any window exists, so throwing here would mean a process that dies with no UI to
/// explain itself — and a missing default language is recoverable (the loader falls back to the
/// first language it discovers) in a way that a failure to start is not.
/// </remarks>
public sealed class AppDefaults
{
    /// <summary>Used when the file is absent, unreadable, or does not name a language.</summary>
    public const string FallbackLanguageCode = "zh-CN";

    /// <summary>Backups kept when none is configured. Zero or less keeps every backup.</summary>
    public const int FallbackBackupRetentionCount = 10;

    private AppDefaults(string defaultLanguageCode, int backupRetentionCount)
    {
        DefaultLanguageCode = defaultLanguageCode;
        BackupRetentionCount = backupRetentionCount;
    }

    /// <summary>
    /// Language a fresh installation starts in, and the fallback for a key missing from the language
    /// actually in use. Not guaranteed to be a language this build ships — the localization loader
    /// checks that and falls back, because a typo here should not stop the application.
    /// </summary>
    public string DefaultLanguageCode { get; }

    /// <summary>
    /// How many safety copies to keep under <c>Backups/</c>. Zero or less keeps every backup.
    /// Applied only after a new backup is written — see <see cref="UserDataPaths.PruneBackups"/>.
    /// </summary>
    public int BackupRetentionCount { get; }

    /// <summary>
    /// Case-insensitive on purpose, and it is load-bearing rather than defensive. This file is
    /// hand-edited, so it is written in the camelCase a human expects of JSON — and
    /// System.Text.Json matches property names CASE-SENSITIVELY by default, so "defaultLanguage"
    /// would not bind to DefaultLanguage at all. The value would come back null and every read
    /// would silently return the fallback, which is exactly what happened until a test used a value
    /// that differed from the fallback.
    /// </summary>
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    public static AppDefaults Load() => Load(SystemSettingsPaths.AppDefaultsFile);

    public static AppDefaults Load(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return Fallback;

            var payload = JsonSerializer.Deserialize<AppDefaultsPayload>(File.ReadAllText(filePath), ReadOptions);
            if (payload is null)
                return Fallback;

            var code = payload.DefaultLanguage;

            return new AppDefaults(
                string.IsNullOrWhiteSpace(code) ? FallbackLanguageCode : code.Trim(),
                payload.BackupRetentionCount ?? FallbackBackupRetentionCount);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Fallback;
        }
    }

    private static AppDefaults Fallback => new(FallbackLanguageCode, FallbackBackupRetentionCount);

    /// <summary>
    /// A positional record rather than a class with settable properties: the deserializer is the
    /// only thing that ever populates it, which a plain auto-property cannot express and static
    /// analysis reads as an unassigned field.
    /// </summary>
    private sealed record AppDefaultsPayload(string? DefaultLanguage, int? BackupRetentionCount);
}
