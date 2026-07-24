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
}
