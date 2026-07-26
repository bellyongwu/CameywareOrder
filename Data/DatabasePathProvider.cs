namespace LeeYongeOrdering.Data;

public static class DatabasePathProvider
{
    public static string AppDataDirectory =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LeeYongeOrdering");

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

    /// <summary>Copies the live database (plus any WAL/SHM sidecars) to <paramref name="targetPath"/>.</summary>
    public static void ExportDatabaseTo(string targetPath)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        System.IO.File.Copy(DatabaseFilePath, targetPath, overwrite: true);
        CopySidecarIfExists(DatabaseFilePath + "-wal", targetPath + "-wal");
        CopySidecarIfExists(DatabaseFilePath + "-shm", targetPath + "-shm");
    }

    /// <summary>
    /// Replaces the live database with <paramref name="sourcePath"/> (plus any matching
    /// WAL/SHM sidecars). The current database is first backed up alongside it with a
    /// timestamped filename so the replaced data can be recovered if needed. Returns the
    /// backup file path, or null if there was no existing database to back up.
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

        System.IO.File.Copy(sourcePath, DatabaseFilePath, overwrite: true);

        DeleteSidecarIfExists(DatabaseFilePath + "-wal");
        DeleteSidecarIfExists(DatabaseFilePath + "-shm");
        CopySidecarIfExists(sourcePath + "-wal", DatabaseFilePath + "-wal");
        CopySidecarIfExists(sourcePath + "-shm", DatabaseFilePath + "-shm");

        return backupPath;
    }

    private static void DeleteSidecarIfExists(string path)
    {
        if (System.IO.File.Exists(path))
            System.IO.File.Delete(path);
    }
}
