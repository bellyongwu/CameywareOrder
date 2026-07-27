using System.IO;
using System.IO.Compression;

namespace CameywareOrder.Data;

public static class DatabasePathProvider
{
    public static string AppDataDirectory =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CameywareOrder");

    public static string DatabaseFilePath => System.IO.Path.Combine(AppDataDirectory, "orders.db");

    public static string ConnectionString => $"Data Source={DatabaseFilePath}";

    public static void EnsureDatabasePathReady()
    {
        System.IO.Directory.CreateDirectory(AppDataDirectory);

        if (System.IO.File.Exists(DatabaseFilePath))
            return;

        foreach (var legacyDbPath in GetLegacyDatabaseCandidates())
        {
            if (!System.IO.File.Exists(legacyDbPath))
                continue;

            CopyDatabaseSet(legacyDbPath, DatabaseFilePath);
            break;
        }
    }

    private static IEnumerable<string> GetLegacyDatabaseCandidates()
    {
        var candidates = new[]
        {
            System.IO.Path.Combine(Environment.CurrentDirectory, "orders.db"),
            System.IO.Path.Combine(AppContext.BaseDirectory, "orders.db")
        };

        return candidates
            .Where(path => !string.Equals(path, DatabaseFilePath, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static void CopyDatabaseSet(string sourceDb, string targetDb)
    {
        System.IO.File.Copy(sourceDb, targetDb, overwrite: false);

        CopySidecarIfExists(sourceDb + "-wal", targetDb + "-wal");
        CopySidecarIfExists(sourceDb + "-shm", targetDb + "-shm");
    }

    private static void CopySidecarIfExists(string sourcePath, string targetPath)
    {
        if (!System.IO.File.Exists(sourcePath))
            return;

        System.IO.File.Copy(sourcePath, targetPath, overwrite: true);
    }

    // --- Import / export ---------------------------------------------------------

    private static string DocumentsRootDirectory => System.IO.Path.Combine(AppDataDirectory, "Documents");

    private const string DatabaseEntryName = "orders.db";

    /// <summary>
    /// Packages the live database (plus any WAL/SHM sidecars) and every attached document
    /// image under <c>Documents/</c> into a single zip so the export is self-contained and
    /// can be copied to another PC without leaving image references dangling.
    /// </summary>
    public static void ExportDatabaseTo(string targetPath)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        if (System.IO.File.Exists(targetPath))
            System.IO.File.Delete(targetPath);

        using var archive = System.IO.Compression.ZipFile.Open(targetPath, System.IO.Compression.ZipArchiveMode.Create);

        archive.CreateEntryFromFile(DatabaseFilePath, DatabaseEntryName);
        AddSidecarToArchive(archive, DatabaseFilePath + "-wal", DatabaseEntryName + "-wal");
        AddSidecarToArchive(archive, DatabaseFilePath + "-shm", DatabaseEntryName + "-shm");

        if (!System.IO.Directory.Exists(DocumentsRootDirectory))
            return;

        foreach (var file in System.IO.Directory.EnumerateFiles(DocumentsRootDirectory, "*", System.IO.SearchOption.AllDirectories))
        {
            var relative = System.IO.Path.GetRelativePath(AppDataDirectory, file).Replace('\\', '/');
            archive.CreateEntryFromFile(file, relative);
        }
    }

    private static void AddSidecarToArchive(System.IO.Compression.ZipArchive archive, string sourcePath, string entryName)
    {
        if (System.IO.File.Exists(sourcePath))
            archive.CreateEntryFromFile(sourcePath, entryName);
    }

    /// <summary>
    /// Replaces the live database and the attached document images with the contents of
    /// <paramref name="sourcePath"/>. Accepts either a package produced by
    /// <see cref="ExportDatabaseTo"/> (zip containing <c>orders.db</c> plus <c>Documents/</c>)
    /// or a legacy raw <c>.db</c> file (database only, for backward compatibility with
    /// exports made before document packaging existed). The current database and documents
    /// folder are both backed up alongside the app data folder first, so a bad import can be
    /// recovered from. Returns the database backup path, or null if there was no existing
    /// database to back up.
    /// </summary>
    public static string? ImportDatabaseFrom(string sourcePath)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        string? backupPath = null;
        if (System.IO.File.Exists(DatabaseFilePath))
        {
            backupPath = System.IO.Path.Combine(AppDataDirectory, $"orders.db.bak-{DateTime.Now:yyyyMMddHHmmss}");
            System.IO.File.Copy(DatabaseFilePath, backupPath, overwrite: true);
        }

        if (TryImportFromPackage(sourcePath, backupPath))
            return backupPath;

        ImportLegacyDatabaseFile(sourcePath);
        return backupPath;
    }

    /// <summary>Attempts to import a zip package produced by <see cref="ExportDatabaseTo"/>. Returns false when
    /// <paramref name="sourcePath"/> is not a valid zip package (falls back to legacy raw-file import).</summary>
    private static bool TryImportFromPackage(string sourcePath, string? databaseBackupPath)
    {
        System.IO.Compression.ZipArchive archive;
        try
        {
            archive = System.IO.Compression.ZipFile.OpenRead(sourcePath);
        }
        catch (InvalidDataException)
        {
            return false;
        }

        using (archive)
        {
            if (archive.GetEntry(DatabaseEntryName) is null)
                return false;

            DeleteSidecarIfExists(DatabaseFilePath + "-wal");
            DeleteSidecarIfExists(DatabaseFilePath + "-shm");
            BackUpExistingDocuments(databaseBackupPath);
            ExtractPackageSafely(archive, AppDataDirectory);
        }

        return true;
    }

    private static void BackUpExistingDocuments(string? databaseBackupPath)
    {
        if (!System.IO.Directory.Exists(DocumentsRootDirectory))
            return;

        var suffix = databaseBackupPath is not null
            ? System.IO.Path.GetFileName(databaseBackupPath).Replace("orders.db.bak-", string.Empty)
            : DateTime.Now.ToString("yyyyMMddHHmmss");
        var backupDirectory = System.IO.Path.Combine(AppDataDirectory, $"Documents.bak-{suffix}");
        System.IO.Directory.Move(DocumentsRootDirectory, backupDirectory);
    }

    /// <summary>Extracts every entry to <paramref name="destinationRoot"/>, rejecting any entry whose
    /// resolved path would escape the destination folder (zip-slip protection).</summary>
    private static void ExtractPackageSafely(System.IO.Compression.ZipArchive archive, string destinationRoot)
    {
        var rootFull = System.IO.Path.GetFullPath(destinationRoot + System.IO.Path.DirectorySeparatorChar);

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue; // directory entry

            var destinationPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(destinationRoot, entry.FullName));
            if (!destinationPath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The backup package contains an invalid entry path.");

            var directory = System.IO.Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory))
                System.IO.Directory.CreateDirectory(directory);

            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }

    private static void ImportLegacyDatabaseFile(string sourcePath)
    {
        System.IO.File.Copy(sourcePath, DatabaseFilePath, overwrite: true);

        DeleteSidecarIfExists(DatabaseFilePath + "-wal");
        DeleteSidecarIfExists(DatabaseFilePath + "-shm");
        CopySidecarIfExists(sourcePath + "-wal", DatabaseFilePath + "-wal");
        CopySidecarIfExists(sourcePath + "-shm", DatabaseFilePath + "-shm");
    }

    private static void DeleteSidecarIfExists(string path)
    {
        if (System.IO.File.Exists(path))
            System.IO.File.Delete(path);
    }
}
