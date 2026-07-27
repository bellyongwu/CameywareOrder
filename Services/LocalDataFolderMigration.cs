using System.IO;
// Required: ImplicitUsings pulls in HotChocolate, which also defines Path, so a bare Path is
// ambiguous here exactly as it is in DocumentStorageService.
using Path = System.IO.Path;

namespace CameywareOrder.Services;

/// <summary>
/// Moves the application's LocalAppData folder from its pre-rebrand name to the current one, once.
/// </summary>
/// <remarks>
/// The product was renamed from "LeeYongeOrdering" to "CameywareOrder". Six components resolve
/// their storage independently under <c>%LocalAppData%\&lt;product&gt;\</c>, so renaming the
/// constant alone would have pointed every one of them at an empty directory: the orders database
/// and its WAL/SHM sidecars, the <c>Documents\</c> image tree, the signed-in accounts file, the
/// branding folder, the measurement-terms config, and the currency/language preferences. An
/// existing installation would have launched looking like a fresh install with no orders in it,
/// which reads as catastrophic data loss even though the files were still on disk.
///
/// Runs before ANY of those paths is touched — in particular before
/// <c>DatabasePathProvider.EnsureDatabasePathReady()</c>, which creates the folder and would
/// otherwise satisfy the "already migrated" test on the very first launch.
/// </remarks>
public static class LocalDataFolderMigration
{
    private const string LegacyFolderName = "LeeYongeOrdering";
    private const string CurrentFolderName = "CameywareOrder";

    /// <summary>
    /// Renames the legacy data folder to the current one when that is what the disk calls for.
    /// Idempotent: after a successful move the current folder exists, so this returns immediately
    /// on every later launch.
    /// </summary>
    /// <returns><c>true</c> when a migration actually happened, for reporting.</returns>
    public static bool EnsureCurrentFolderName()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var current = Path.Combine(root, CurrentFolderName);
        var legacy = Path.Combine(root, LegacyFolderName);

        // Real data already lives under the current name: migrated previously, or this is an
        // install that never knew the old name. Either way, never touch it.
        if (Directory.Exists(current) && !IsEmpty(current))
            return false;

        if (!Directory.Exists(legacy))
            return false;

        try
        {
            // An EMPTY current folder means the renamed build was launched once before this
            // migration existed and created a placeholder. Removing it lets the move land;
            // Directory.Move refuses an existing destination. Only ever an empty directory is
            // deleted here — the guard above protects anything with content in it.
            if (Directory.Exists(current))
                Directory.Delete(current);

            // Move rather than copy: atomic within a volume, and the Documents tree can hold
            // hundreds of images. If it throws, the legacy folder is left exactly as it was, so a
            // failure costs a startup rather than any data.
            Directory.Move(legacy, current);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Deliberately fatal, and deliberately not localized (the string table may not be
            // loaded yet). Continuing would create a fresh empty folder and present the shop with
            // an empty order list — the one outcome worse than refusing to start, because it looks
            // like the data is gone. Say plainly where the data is and what most likely blocked it.
            throw new InvalidOperationException(
                $"Could not move the application data folder from '{legacy}' to '{current}'. " +
                "Your data has NOT been lost — it is still in the original folder. " +
                "This is usually caused by another copy of the application still running, or by a " +
                "file in that folder being open. Close any other copy and start again. " +
                $"Underlying error: {ex.Message}",
                ex);
        }
    }

    private static bool IsEmpty(string directory)
        => !Directory.EnumerateFileSystemEntries(directory).Any();
}
