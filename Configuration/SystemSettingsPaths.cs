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
    private const string DefaultsFolder = "Defaults";

    /// <summary>Per-language string tables, one document per language.</summary>
    public static string LanguagesDirectory => Path.Combine(SystemDirectory, "Languages");

    /// <summary>Defaults that ship with the build (see app-defaults.json).</summary>
    public static string DefaultsDirectory => Path.Combine(SystemDirectory, DefaultsFolder);

    public static string AppDefaultsFile => DefaultsFile("app-defaults.json");

    /// <summary>Tax presets keyed by store location (see tax-jurisdictions.json).</summary>
    public static string TaxJurisdictionsFile => DefaultsFile("tax-jurisdictions.json");

    /// <summary>Dial codes and national number lengths per country (see phone-countries.json).</summary>
    public static string PhoneCountriesFile => DefaultsFile("phone-countries.json");

    /// <summary>
    /// Locates one shipped defaults file, probing the same two roots as <see cref="SystemDirectory"/>
    /// but asking whether THE FILE is there rather than whether a folder is.
    /// </summary>
    /// <remarks>
    /// The distinction is not academic. <see cref="SystemDirectory"/> returns the first root that has
    /// a <c>Settings/System</c> folder at all, so a deployment carrying that folder with only SOME of
    /// its files in it wins the probe and every missing file then reads as absent — and each loader
    /// answers a missing file by degrading, silently, to its built-in fallback. That is exactly how it
    /// showed up: a harness whose output directory held a partial copy (app-defaults.json alone) read
    /// back ONE phone country instead of six, so a stored "+86 …" matched no dial code and came out
    /// with the home market's "+1" pasted in front of it. Nothing threw; the number was just wrong.
    ///
    /// Falls back to the base-directory path when neither root has the file, so a caller reporting
    /// "not found" still names a path a person can go and look at.
    /// </remarks>
    private static string DefaultsFile(string fileName)
    {
        var beside = Path.Combine(AppContext.BaseDirectory, SettingsFolder, SystemFolder, DefaultsFolder, fileName);
        if (File.Exists(beside))
            return beside;

        var working = Path.Combine(Environment.CurrentDirectory, SettingsFolder, SystemFolder, DefaultsFolder, fileName);
        return File.Exists(working) ? working : beside;
    }

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
