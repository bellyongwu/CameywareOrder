using System.Diagnostics;
using System.IO;
using Path = System.IO.Path;

namespace CameywareOrder.Configuration;

/// <summary>
/// Every path under <c>%LOCALAPPDATA%\CameywareOrder</c> — the per-installation state the
/// application WRITES.
/// </summary>
/// <remarks>
/// The counterpart to <see cref="SystemSettingsPaths"/>, and the distinction is the important part:
/// that one is shipped configuration, read-only and replaced wholesale by an upgrade; this one is
/// the user's own data, and must SURVIVE an upgrade. Anything added here needs a migration story.
///
/// It exists because the folder name was previously spelled out in SIX independent places — the
/// credential store, the currency store, the language preference store, the measurement terms
/// service, the branding store and the database provider each did their own
/// <c>Path.Combine(GetFolderPath(LocalApplicationData), "CameywareOrder")</c>. The product has
/// already been renamed once (LeeYongeOrdering → CameywareOrder, see LocalDataFolderMigration), and
/// six copies of a folder name is six chances to miss one next time.
/// </remarks>
public static class UserDataPaths
{
    private const string FolderName = "CameywareOrder";

    /// <summary>Root of the per-installation data folder.</summary>
    public static string Root =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderName);

    /// <summary>Per-machine settings files: credentials, currency, language preference.</summary>
    public static string ConfigDirectory => Path.Combine(Root, "Config");

    /// <summary>Safety copies taken before a destructive import. See <see cref="PruneBackups"/>.</summary>
    public static string BackupsDirectory => Path.Combine(Root, "Backups");

    /// <summary>
    /// Attached document images.
    /// </summary>
    /// <remarks>
    /// Deliberately still at the root, and it must stay there. <c>DatabasePathProvider</c> writes
    /// export packages with entry paths RELATIVE TO <see cref="Root"/> and extracts them the same
    /// way, so "Documents/…" is baked into every export zip a user already has. Moving this folder
    /// would make old exports extract to a stale location — the on-disk layout here is a data
    /// interchange format, not just a folder.
    /// </remarks>
    public static string DocumentsDirectory => Path.Combine(Root, "Documents");

    /// <summary>Per-shop receipt branding, keyed on Shop.PublicId. Already a folder; left in place.</summary>
    public static string BrandingDirectory => Path.Combine(Root, "Branding");

    /// <summary>
    /// The SQLite database. Left at the root on purpose: it is named in the export package as
    /// <c>orders.db</c> at top level, and every connection string in the application resolves
    /// through it. Tidiness is not worth reopening either.
    /// </summary>
    public static string DatabaseFile => Path.Combine(Root, "orders.db");

    /// <summary>Per-shop measurement terms. Left at the root: keyed by file NAME on Shop.PublicId.</summary>
    public static string ShopDataDirectory => Root;

    /// <summary>
    /// Full path of a settings file under <see cref="ConfigDirectory"/>, moving it out of the flat
    /// root the first time it is asked for.
    /// </summary>
    /// <remarks>
    /// Migration is lazy and per file rather than a bulk sweep, and it FALLS BACK rather than
    /// throwing: if the move cannot be done — the file is locked, the disk is read-only, another
    /// copy of the application is running — the old path is returned and the file keeps being read
    /// where it is. Nothing here is allowed to make credentials.json unreadable; being unable to
    /// tidy up is not a reason to lock somebody out of their own application.
    /// </remarks>
    public static string ResolveConfigFile(string fileName) => ResolveConfigFile(fileName, Root);

    /// <summary>
    /// As <see cref="ResolveConfigFile(string)"/>, against an explicit data root.
    /// </summary>
    /// <remarks>
    /// The root is a parameter — rather than every operation reaching for the real
    /// <see cref="Root"/> — so this logic can be exercised against a throwaway folder. The
    /// alternative was a test-only seam on a class that decides where the user's credentials live,
    /// and a migration that has only ever run against the machine it must not break is not one worth
    /// shipping.
    /// </remarks>
    public static string ResolveConfigFile(string fileName, string root)
    {
        var configDirectory = Path.Combine(root, "Config");
        var current = Path.Combine(configDirectory, fileName);
        if (File.Exists(current))
            return current;

        var legacy = Path.Combine(root, fileName);
        if (!File.Exists(legacy))
            return current; // Nothing to migrate — a fresh install writes straight to the new place.

        try
        {
            Directory.CreateDirectory(configDirectory);
            File.Move(legacy, current);
            return current;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"[userdata] could not move {fileName} into Config: {ex.Message}");
            return legacy;
        }
    }

    /// <summary>
    /// Moves the safety copies left at the root by earlier versions into <see cref="BackupsDirectory"/>.
    /// </summary>
    /// <remarks>
    /// A sweep rather than lazy migration, because unlike a settings file nothing ever reads these —
    /// they would simply pile up at the root forever, which is what they had been doing (23 of them
    /// on the machine this was written for). Nothing is DELETED here: an old backup is the user's,
    /// not ours to discard on their behalf just because we reorganised around it. Deletion happens
    /// only through <see cref="PruneBackups"/>, and only after a new backup is created.
    /// </remarks>
    public static void SweepLegacyBackups() => SweepLegacyBackups(Root);

    /// <summary>As <see cref="SweepLegacyBackups()"/>, against an explicit data root.</summary>
    public static void SweepLegacyBackups(string root)
    {
        if (!Directory.Exists(root))
            return;

        try
        {
            var files = Directory.GetFiles(root, "orders.db.bak-*", SearchOption.TopDirectoryOnly);
            var folders = Directory.GetDirectories(root, "Documents.bak-*", SearchOption.TopDirectoryOnly);

            if (files.Length == 0 && folders.Length == 0)
                return;

            var backups = Path.Combine(root, "Backups");
            Directory.CreateDirectory(backups);

            foreach (var file in files)
                MoveIfDestinationFree(file, Path.Combine(backups, Path.GetFileName(file)), isDirectory: false);

            foreach (var folder in folders)
                MoveIfDestinationFree(folder, Path.Combine(backups, Path.GetFileName(folder)), isDirectory: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Non-fatal by design: failing to tidy up must never stop the application starting.
            Trace.TraceWarning($"[userdata] backup sweep incomplete: {ex.Message}");
        }
    }

    /// <summary>
    /// Keeps the <paramref name="keep"/> most recent backups of each kind and deletes the rest.
    /// A <paramref name="keep"/> of zero or less keeps everything.
    /// </summary>
    /// <remarks>
    /// Ordered by last-write time, NOT by the timestamp in the name: one of the backups in the wild
    /// is called <c>orders.db.bak-preShopRules</c>, so name parsing would have to decide what to do
    /// with a suffix that is not a date — and guessing wrong deletes the wrong backup.
    ///
    /// Called only after a NEW backup has been written, so the count is bounded going forward
    /// without a startup sweep ever deleting anything the user has not just replaced.
    /// </remarks>
    public static void PruneBackups(int keep) => PruneBackups(keep, Root);

    /// <summary>As <see cref="PruneBackups(int)"/>, against an explicit data root.</summary>
    public static void PruneBackups(int keep, string root)
    {
        var backups = Path.Combine(root, "Backups");
        if (keep <= 0 || !Directory.Exists(backups))
            return;

        try
        {
            Prune(Directory.GetFiles(backups, "orders.db.bak-*"), File.GetLastWriteTimeUtc, File.Delete);
            Prune(
                Directory.GetDirectories(backups, "Documents.bak-*"),
                Directory.GetLastWriteTimeUtc,
                path => Directory.Delete(path, recursive: true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"[userdata] backup pruning incomplete: {ex.Message}");
        }

        void Prune(string[] paths, Func<string, DateTime> stamp, Action<string> delete)
        {
            foreach (var stale in paths.OrderByDescending(stamp).Skip(keep))
            {
                delete(stale);
                Trace.TraceInformation($"[userdata] pruned old backup {Path.GetFileName(stale)}");
            }
        }
    }

    private static void MoveIfDestinationFree(string source, string destination, bool isDirectory)
    {
        // Never overwrite: a name collision here would mean discarding one of two safety copies, and
        // leaving the stray at the root is a far better outcome than losing it.
        if (isDirectory ? Directory.Exists(destination) : File.Exists(destination))
            return;

        if (isDirectory)
            Directory.Move(source, destination);
        else
            File.Move(source, destination);
    }
}
