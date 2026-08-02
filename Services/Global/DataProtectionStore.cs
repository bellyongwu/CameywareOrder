using System.IO;
using System.Text.Json;
using CameywareOrder.Configuration;
using CameywareOrder.Models;
using Path = System.IO.Path;

namespace CameywareOrder.Services;

/// <summary>
/// Owns <c>Config/data-protection.json</c> — this installation's backup schedule and recycle-bin
/// retention.
/// </summary>
/// <remarks>
/// A singleton holding ONE instance, because two callers reading the file separately would each get
/// their own copy and the second save would overwrite the first's changes: the startup backup writes
/// <see cref="DataProtectionSettings.LastBackupUtc"/> while the settings panel may be holding an
/// older snapshot of everything else.
///
/// Reading is defensive in the way every settings loader here is: a missing or corrupt file yields
/// the defaults rather than throwing, because this is read during startup before any window exists
/// and a shop must never be unable to open because a JSON file lost a brace. Writing is best-effort
/// for the same reason — failing to record when the last backup ran must not stop the backup, and
/// must certainly not stop the application.
///
/// The retention count is SEEDED from the shipped <c>app-defaults.json</c> the first time the file is
/// written, so an installation that had already tuned that number keeps it. After that the shipped
/// value is not consulted again: it is a default, and a default that kept overriding the user's
/// choice on every upgrade would not be one.
/// </remarks>
public sealed class DataProtectionStore
{
    private const string FileName = "data-protection.json";

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private DataProtectionSettings? _settings;

    private DataProtectionStore()
    {
    }

    public static DataProtectionStore Instance { get; } = new();

    /// <summary>Raised after <see cref="Save"/>, so an open panel can re-read what it now says.</summary>
    public event EventHandler? SettingsChanged;

    /// <summary>The live settings. Loaded once, then held — see the remarks on the class.</summary>
    public DataProtectionSettings Settings => _settings ??= Load();

    /// <summary>
    /// Replaces the stored settings and persists them. Takes a whole object rather than exposing
    /// setters so a partial write cannot leave the file describing a state nobody chose.
    /// </summary>
    public void Save(DataProtectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
        Persist(settings);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Records that a backup has just run, without disturbing anything else on the settings.
    /// </summary>
    /// <remarks>
    /// Deliberately not "read, mutate, Save(clone)" at the call site. The backup runs during startup
    /// and the settings panel can be open later in the same session holding its own edited copy; a
    /// call site that rebuilt the whole object from a stale read would write the old schedule back.
    /// Mutating the one live instance is what keeps the two in step.
    /// </remarks>
    public void RecordBackupRun(DateTime whenUtc)
    {
        Settings.LastBackupUtc = whenUtc;
        Persist(Settings);
    }

    private static string FilePath => UserDataPaths.ResolveConfigFile(FileName);

    private static DataProtectionSettings Load()
    {
        try
        {
            var path = FilePath;
            if (!File.Exists(path))
                return CreateDefault();

            return JsonSerializer.Deserialize<DataProtectionSettings>(File.ReadAllText(path), ReadOptions)
                   ?? CreateDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return CreateDefault();
        }
    }

    /// <summary>The settings a machine that has never been asked starts with.</summary>
    private static DataProtectionSettings CreateDefault()
        => new() { BackupRetentionCount = AppDefaults.Load().BackupRetentionCount };

    private static void Persist(DataProtectionSettings settings)
    {
        try
        {
            var path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(settings, WriteOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort. Losing the schedule costs one extra backup on the next launch; refusing to
            // start because a settings file could not be written costs the shop its morning.
        }
    }
}
