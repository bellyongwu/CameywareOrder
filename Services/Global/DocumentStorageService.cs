using System.IO;
using CameywareOrder.Data;
using CameywareOrder.Models;
using Path = System.IO.Path;
using CameywareOrder.Configuration;

namespace CameywareOrder.Services;

// Global helper for storing, retrieving, exporting and deleting the image files
// attached to custom-made records. Files live next to the database under AppData so
// they survive app updates; records persist only a lightweight reference.
public static class DocumentStorageService
{
    private static readonly string[] AllowedExtensions =
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp"
    };

    public static string RootDirectory =>
        Path.Combine(UserDataPaths.DocumentsDirectory, "CustomMade");

    public static string ImageFileFilter =>
        "Image files|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp|All files|*.*";

    public static bool IsSupportedImage(string path)
    {
        var ext = Path.GetExtension(path);
        return !string.IsNullOrEmpty(ext)
            && AllowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    public static string GetFullPath(string storedFileName) =>
        Path.Combine(RootDirectory, storedFileName);

    public static bool Exists(CustomMadeDocument document) =>
        !string.IsNullOrWhiteSpace(document.StoredFileName)
        && File.Exists(GetFullPath(document.StoredFileName));

    // Copies the chosen image into the store and returns a new reference. The image
    // bytes are stored under a fresh id-based name so originals never collide.
    public static CustomMadeDocument Import(string sourcePath, CustomMadeDocumentCategory category)
    {
        Directory.CreateDirectory(RootDirectory);

        var id = Guid.NewGuid();
        var extension = Path.GetExtension(sourcePath);
        var storedFileName = id.ToString("N") + extension;

        File.Copy(sourcePath, Path.Combine(RootDirectory, storedFileName), overwrite: true);

        return new CustomMadeDocument
        {
            Id = id,
            Category = category,
            FileName = Path.GetFileName(sourcePath),
            StoredFileName = storedFileName,
            UploadedAtUtc = DateTime.UtcNow
        };
    }

    // Copies the stored image out to a user-chosen location (download).
    public static void Export(CustomMadeDocument document, string destinationPath)
    {
        var source = GetFullPath(document.StoredFileName);
        if (File.Exists(source))
            File.Copy(source, destinationPath, overwrite: true);
    }

    public static void Delete(CustomMadeDocument document)
    {
        if (string.IsNullOrWhiteSpace(document.StoredFileName))
            return;

        var path = GetFullPath(document.StoredFileName);
        if (File.Exists(path))
            File.Delete(path);
    }

    public static void DeleteByStoredName(string storedFileName)
    {
        if (string.IsNullOrWhiteSpace(storedFileName))
            return;

        var path = GetFullPath(storedFileName);
        if (File.Exists(path))
            File.Delete(path);
    }
}
