using System.Diagnostics;
using System.IO;
using CameywareOrder.Configuration;
using CameywareOrder.Data;
using Path = System.IO.Path;

namespace CameywareOrder.Services;

/// <summary>
/// The application's own safety copies: takes one when it is due, lists what is there, and puts one
/// back.
/// </summary>
/// <remarks>
/// Before v8.0 a backup was taken in exactly one place — immediately before a destructive import —
/// so an installation that never touched Import/Export had none at all. That is the shape a local
/// SQLite deployment least survives: one disk failure, or one confirmed delete of the wrong rows,
/// and the shop's whole trading history is gone with nothing to go back to.
///
/// It writes NO new file format. A backup is exactly the package
/// <see cref="DatabasePathProvider.ExportDatabaseTo"/> already produces — the database, its WAL/SHM
/// sidecars and the whole <c>Documents/</c> image tree in one zip — and a restore is exactly
/// <see cref="DatabasePathProvider.ImportDatabaseFrom"/>, which already backs up what it replaces
/// and already guards against zip-slip. A second copy routine would be a second thing to keep in
/// step with the schema, and the one that runs unattended is the one nobody would notice drifting.
///
/// Everything here is best-effort and swallows I/O failures. A backup that cannot be written must
/// never stop the shop opening; it reports through <see cref="BackupResult"/> so a caller that has a
/// screen to say so on can, and the ones that run during startup do not have one.
/// </remarks>
public static class BackupService
{
    private const string FilePrefix = "backup-";
    private const string FileExtension = ".zip";

    /// <summary>
    /// Takes a backup if the schedule says one is due, and returns what happened.
    /// </summary>
    /// <remarks>
    /// Called from startup, BEFORE the main window opens and therefore before anybody can be typing
    /// into the database. A backup taken while the application is writing is a backup of a database
    /// mid-transaction, and the moment to find that out is not the moment you need it.
    /// </remarks>
    public static BackupResult RunIfDue(DateTime nowUtc)
    {
        var settings = DataProtectionStore.Instance.Settings;
        if (!settings.IsBackupDue(nowUtc))
            return BackupResult.NotDue;

        return RunNow(nowUtc);
    }

    /// <summary>Takes a backup regardless of the schedule — the panel's "Back up now".</summary>
    public static BackupResult RunNow(DateTime nowUtc)
    {
        var store = DataProtectionStore.Instance;

        try
        {
            Directory.CreateDirectory(UserDataPaths.BackupsDirectory);

            var path = Path.Combine(
                UserDataPaths.BackupsDirectory,
                FilePrefix + nowUtc.ToLocalTime().ToString("yyyyMMdd-HHmmss") + FileExtension);

            DatabasePathProvider.ExportDatabaseTo(path);

            // Recorded BEFORE pruning, so a machine whose disk fills mid-prune still knows it has a
            // fresh copy and does not spend every launch rewriting one.
            store.RecordBackupRun(nowUtc);

            // Only ever after a new copy exists — the standing rule for this folder. Pruning first
            // would delete the oldest backup to make room for one that might then fail to write.
            UserDataPaths.PruneBackups(store.Settings.BackupRetentionCount);

            return BackupResult.Written(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Trace.TraceWarning($"[backup] could not write a backup: {ex.Message}");
            return BackupResult.Failed(ex.Message);
        }
    }

    /// <summary>
    /// Every safety copy on this machine, newest first — the scheduled packages and the copies taken
    /// before an import alike.
    /// </summary>
    /// <remarks>
    /// Ordered by WRITE TIME, never by the timestamp in the name. One backup in the wild is called
    /// <c>orders.db.bak-preShopRules</c>, so name parsing has to guess at a suffix that is not a
    /// date — and in a list the user restores from, guessing wrong offers the wrong file.
    /// </remarks>
    public static IReadOnlyList<BackupEntry> List()
    {
        var directory = UserDataPaths.BackupsDirectory;
        if (!Directory.Exists(directory))
            return Array.Empty<BackupEntry>();

        try
        {
            var packages = Directory.EnumerateFiles(directory, UserDataPaths.BackupPackagePattern)
                .Select(path => Describe(path, BackupKind.Package));

            var preImport = Directory.EnumerateFiles(directory, "orders.db.bak-*")
                .Select(path => Describe(path, BackupKind.PreImportDatabase));

            return packages.Concat(preImport)
                .OrderByDescending(entry => entry.TakenAtUtc)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"[backup] could not list backups: {ex.Message}");
            return Array.Empty<BackupEntry>();
        }
    }

    /// <summary>
    /// Puts a backup back, replacing the live database and document images.
    /// </summary>
    /// <remarks>
    /// Straight through <see cref="DatabasePathProvider.ImportDatabaseFrom"/>, which is the same path
    /// the Import menu uses: it takes a copy of what it is about to overwrite first, so a restore of
    /// the wrong file is itself undoable, and it accepts both kinds this service lists — the package
    /// and the bare pre-import database — with no branch here.
    ///
    /// The CALLER owns the confirmation and owns telling the user to reopen the shop. This performs
    /// the swap and nothing else; a service that put a modal in front of a file operation could not
    /// be driven by a harness, which is the split the rest of this codebase already makes.
    /// </remarks>
    public static BackupResult Restore(BackupEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        try
        {
            if (!File.Exists(entry.Path))
                return BackupResult.Failed(entry.FileName);

            DatabasePathProvider.ImportDatabaseFrom(entry.Path);
            return BackupResult.Written(entry.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Trace.TraceWarning($"[backup] could not restore {entry.FileName}: {ex.Message}");
            return BackupResult.Failed(ex.Message);
        }
    }

    /// <summary>Deletes one safety copy. Best-effort: a file already gone is the state this wanted.</summary>
    public static bool Delete(BackupEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        try
        {
            if (File.Exists(entry.Path))
                File.Delete(entry.Path);

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"[backup] could not delete {entry.FileName}: {ex.Message}");
            return false;
        }
    }

    private static BackupEntry Describe(string path, BackupKind kind)
    {
        var info = new FileInfo(path);
        return new BackupEntry(path, info.Name, File.GetLastWriteTimeUtc(path), info.Length, kind);
    }
}

/// <summary>Which sort of safety copy a file is — they restore identically but read differently.</summary>
public enum BackupKind
{
    /// <summary>A full package: the database, its sidecars and every attached image.</summary>
    Package,

    /// <summary>The bare database copied automatically before an import replaced it.</summary>
    PreImportDatabase
}

/// <summary>One safety copy on disk, as the panel lists it.</summary>
public sealed record BackupEntry(
    string Path, string FileName, DateTime TakenAtUtc, long SizeBytes, BackupKind Kind)
{
    /// <summary>When it was taken, in the shop's own time — what the list actually shows.</summary>
    public DateTime TakenAtLocal => TakenAtUtc.ToLocalTime();
}

/// <summary>
/// What a backup or restore did. Three outcomes rather than a bool, because "not due" is a success
/// that wrote nothing and a caller reporting it as a failure would be wrong twice a day.
/// </summary>
public readonly record struct BackupResult(bool Ran, bool Succeeded, string Detail)
{
    public static BackupResult NotDue => new(false, true, string.Empty);

    public static BackupResult Written(string path) => new(true, true, path);

    public static BackupResult Failed(string detail) => new(true, false, detail);
}
