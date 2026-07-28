using System.IO;
// HotChocolate contributes a global `Path` type; alias it, as the rest of the codebase does.
using Path = System.IO.Path;

namespace CameywareOrder.Configuration;

/// <summary>
/// Locates the <c>Settings/System</c> tree that ships alongside the executable.
/// </summary>
/// <remarks>
/// This is SHIPPED configuration, and the distinction from per-installation state matters enough to
/// state: everything under here is read-only, versioned in git, and replaced wholesale by an
/// upgrade. Anything the application WRITES — the chosen language, credentials, the database, a
/// shop's branding — belongs under <c>%LOCALAPPDATA%\CameywareOrder</c> instead, because it has to
/// survive an upgrade rather than be overwritten by one.
///
/// The two probe locations mirror what the single-file loader did before the split: the app's own
/// directory first, then the working directory, which is what makes `dotnet run` and a published
/// build both work.
/// </remarks>
public static class SystemSettingsPaths
{
    private const string SettingsFolder = "Settings";
    private const string SystemFolder = "System";

    /// <summary>Per-language string tables, one document per language.</summary>
    public static string LanguagesDirectory => Path.Combine(SystemDirectory, "Languages");

    /// <summary>Defaults that ship with the build (see app-defaults.json).</summary>
    public static string DefaultsDirectory => Path.Combine(SystemDirectory, "Defaults");

    public static string AppDefaultsFile => Path.Combine(DefaultsDirectory, "app-defaults.json");

    /// <summary>
    /// The <c>Settings/System</c> directory. Resolved by probing rather than assumed, and it returns
    /// the base-directory path even when nothing is found so the caller's own "does this exist"
    /// check reports a path a human can act on.
    /// </summary>
    public static string SystemDirectory
    {
        get
        {
            var beside = Path.Combine(AppContext.BaseDirectory, SettingsFolder, SystemFolder);
            if (Directory.Exists(beside))
                return beside;

            var working = Path.Combine(Environment.CurrentDirectory, SettingsFolder, SystemFolder);
            if (Directory.Exists(working))
                return working;

            return beside;
        }
    }
}
