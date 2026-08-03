using System.IO;
using System.Text.Json;
using CameywareOrder.Configuration;
using Path = System.IO.Path;

namespace CameywareOrder.Services;

/// <summary>
/// This installation's answer to "may anything outside the application talk to it".
/// </summary>
/// <remarks>
/// One question today: whether the in-process GraphQL server runs at all.
///
/// **It defaults to OFF, and that is the whole point of the file.** The server had been started on
/// every launch, listening on a local port, performing no authentication and no capability check —
/// so anything able to reach the port could read every customer's details and delete orders,
/// regardless of who was signed in or whether anybody was. The endpoint was printed in the status
/// bar, which advertised it to every person who walked past the counter.
///
/// Turning it on is now a deliberate act: a line in <c>Config/integrations.json</c>. There is no UI
/// for it on purpose — nothing the shop uses consumes the API, so the only person who would ever
/// want it is somebody integrating another system, and that person can edit a JSON file. A switch on
/// a settings screen would mostly serve to get clicked by accident.
///
/// Even when it IS on, every resolver now checks the signed-in user's capabilities — see
/// <c>GraphQL/ApiAuthorization</c>. Off-by-default and authorized are two separate defences and this
/// is only the first.
///
/// Reading is defensive in the way every settings loader here is: a missing or corrupt file yields
/// the defaults rather than throwing, because it is read during startup before any window exists.
/// And the defensive answer is the SAFE one — a file that cannot be parsed leaves the API off.
/// </remarks>
public sealed class IntegrationSettingsStore
{
    private const string FileName = "integrations.json";

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private IntegrationSettings? _settings;

    private IntegrationSettingsStore()
    {
    }

    public static IntegrationSettingsStore Instance { get; } = new();

    /// <summary>The live settings. Loaded once, then held.</summary>
    public IntegrationSettings Settings => _settings ??= Load();

    /// <summary>Replaces the stored settings and persists them.</summary>
    public void Save(IntegrationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
        Persist(settings);
    }

    private static string FilePath => UserDataPaths.ResolveConfigFile(FileName);

    private static IntegrationSettings Load()
    {
        try
        {
            var path = FilePath;
            if (!File.Exists(path))
                return new IntegrationSettings();

            return JsonSerializer.Deserialize<IntegrationSettings>(File.ReadAllText(path), ReadOptions)
                   ?? new IntegrationSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A file nobody can read must not be treated as permission to open a port.
            return new IntegrationSettings();
        }
    }

    private static void Persist(IntegrationSettings settings)
    {
        try
        {
            var path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(settings, WriteOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort, like every settings write here: the application must still start.
        }
    }
}

/// <summary>What this installation lets other systems do. See <see cref="IntegrationSettingsStore"/>.</summary>
public sealed class IntegrationSettings
{
    /// <summary>
    /// Whether the in-process GraphQL server is started. FALSE unless somebody has said otherwise —
    /// see the remarks on <see cref="IntegrationSettingsStore"/> for why the default is the whole
    /// point.
    /// </summary>
    public bool GraphQlApiEnabled { get; set; }
}
