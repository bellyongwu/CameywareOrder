using System.IO;
using System.IO.Compression;
using System.Text.Json;
using CameywareOrder.Configuration;
using CameywareOrder.Data;
using CameywareOrder.Models;
using Microsoft.EntityFrameworkCore;

// ImplicitUsings pulls in HotChocolate, whose `Path` type makes a bare `Path` ambiguous. Same alias,
// for the same reason, as DocumentStorageService — see the note there.
using Path = System.IO.Path;

namespace CameywareOrder.Services;

/// <summary>
/// Moves SELECTED shops in and out of a single zip: the "download all data" export, and the file a
/// restore reads back.
/// </summary>
/// <remarks>
/// Deliberately not <c>DatabasePathProvider.ExportDatabaseTo</c>, which packages the whole database
/// file. That is the right tool for "back up this installation" and the wrong one here: it cannot carry
/// two shops out of five, and importing it REPLACES the live database, so restoring one deleted shop
/// would take every other shop with it. This works in rows, so an export is a subset and an import is
/// additive.
///
/// Keyed on <see cref="Shop.PublicId"/> throughout, never <c>Shop.Id</c>. Local ids are autoincrement
/// values that differ per machine, so an archive carrying them would re-parent orders on import; the
/// importer allocates fresh ids and remaps. That is the same rule the per-shop FILES follow, and the
/// reason they are named after the PublicId too.
/// </remarks>
public static class ShopArchive
{
    private const string ManifestEntry = "manifest.json";
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Writes the given shops, their orders and their per-shop files to <paramref name="targetPath"/>.</summary>
    public static ShopArchiveSummary Export(AppDbContext db, IReadOnlyList<Shop> shops, string targetPath)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(shops);

        var shopIds = shops.Select(shop => shop.Id).ToList();

        // IgnoreQueryFilters: these are other shops than the open one, and the filter would return none.
        var orders = db.Orders.AsNoTracking().IgnoreQueryFilters()
            .Include(order => order.Items)
            .Where(order => shopIds.Contains(order.ShopId))
            .ToList();

        if (File.Exists(targetPath))
            File.Delete(targetPath);

        using var archive = ZipFile.Open(targetPath, ZipArchiveMode.Create);

        var payload = new ArchivePayload
        {
            Version = CurrentVersion,
            ExportedAtUtc = DateTime.UtcNow,
            Shops = shops.Select(shop => new ArchivedShop
            {
                Shop = shop,
                Orders = orders.Where(order => order.ShopId == shop.Id).ToList(),
            }).ToList(),
        };

        WriteEntry(archive, ManifestEntry, JsonSerializer.Serialize(payload, WriteOptions));

        foreach (var shop in shops)
            AddPerShopFiles(archive, shop);

        return Describe(payload);
    }

    /// <summary>
    /// Reads an archive's manifest and reports what it holds, WITHOUT touching the installation.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Import"/> on purpose, and the same shape `GlobalSettingsPackage.TryRead`
    /// uses: the confirmation has to be able to say "this file holds 2 shops and 44 orders" before the
    /// user agrees to anything, and offering a destructive confirm for a file that turns out not to be an
    /// archive at all is how people learn to click through confirmations.
    /// </remarks>
    public static ShopArchiveSummary? TryRead(string sourcePath)
    {
        try
        {
            var payload = ReadPayload(sourcePath);
            return payload is null ? null : Describe(payload);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Restores every shop in the archive that this installation does not already have, with its orders
    /// and its per-shop files. Additive: nothing existing is removed or overwritten.
    /// </summary>
    /// <remarks>
    /// A shop whose <see cref="Shop.PublicId"/> is already present is SKIPPED, not merged and not
    /// duplicated. Merging would have to decide which side wins field by field, and duplicating would
    /// leave two shops sharing the per-shop file name — so the archive would hand one of them the
    /// other's branding. Skipping is the only outcome that cannot corrupt what is already there, and the
    /// count comes back so the panel can say how many were skipped and why.
    /// </remarks>
    public static ShopRestoreResult Import(AppDbContext db, string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(db);

        var payload = ReadPayload(sourcePath);
        if (payload is null)
            return new ShopRestoreResult(0, 0, 0);

        var existing = db.Shops.AsNoTracking().Select(shop => shop.PublicId).ToHashSet();
        var restoredShops = 0;
        var restoredOrders = 0;
        var skipped = 0;

        // One suppression scope around the whole restore: the imported orders already carry the shop,
        // currency and pricing mode they were taken under. See AppDbContext.SuppressShopStamping.
        using (db.SuppressShopStamping())
        {
            foreach (var archived in payload.Shops ?? new List<ArchivedShop>())
            {
                if (archived.Shop is null)
                    continue;

                if (existing.Contains(archived.Shop.PublicId))
                {
                    skipped++;
                    continue;
                }

                var shop = archived.Shop;
                shop.Id = 0;
                db.Shops.Add(shop);
                db.SaveChanges();

                foreach (var order in archived.Orders ?? new List<Order>())
                {
                    order.Id = 0;
                    order.ShopId = shop.Id;

                    foreach (var item in order.Items)
                    {
                        item.Id = 0;
                        item.OrderId = 0;
                    }

                    db.Orders.Add(order);
                    restoredOrders++;
                }

                db.SaveChanges();
                RestorePerShopFiles(sourcePath, shop);
                restoredShops++;
            }
        }

        return new ShopRestoreResult(restoredShops, restoredOrders, skipped);
    }

    // ── the zip ───────────────────────────────────────────────────────────────────────────────────

    private static ArchivePayload? ReadPayload(string sourcePath)
    {
        if (!File.Exists(sourcePath))
            return null;

        using var archive = ZipFile.OpenRead(sourcePath);
        var manifest = archive.GetEntry(ManifestEntry);
        if (manifest is null)
            return null;

        using var reader = new StreamReader(manifest.Open());
        var payload = JsonSerializer.Deserialize<ArchivePayload>(reader.ReadToEnd(), ReadOptions);

        // A newer archive may hold fields this build cannot honour; refusing is better than importing
        // half of it and reporting success.
        return payload?.Version > CurrentVersion ? null : payload;
    }

    private static void AddPerShopFiles(ZipArchive archive, Shop shop)
    {
        var terms = MeasurementTermsService.FilePathFor(shop);
        if (File.Exists(terms))
            archive.CreateEntryFromFile(terms, FileRoot(shop) + "measurement-terms.json");

        var branding = ReceiptBrandingStore.DirectoryFor(shop);
        if (!Directory.Exists(branding))
            return;

        foreach (var file in Directory.EnumerateFiles(branding, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(branding, file).Replace('\\', '/');
            archive.CreateEntryFromFile(file, FileRoot(shop) + "branding/" + relative);
        }
    }

    private static void RestorePerShopFiles(string sourcePath, Shop shop)
    {
        using var archive = ZipFile.OpenRead(sourcePath);
        var root = FileRoot(shop);

        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.StartsWith(root, StringComparison.OrdinalIgnoreCase) || entry.Length == 0)
                continue;

            var relative = entry.FullName[root.Length..];
            string target;

            if (relative.Equals("measurement-terms.json", StringComparison.OrdinalIgnoreCase))
            {
                target = MeasurementTermsService.FilePathFor(shop);
            }
            else
            {
                var withinBranding = relative.StartsWith("branding/", StringComparison.OrdinalIgnoreCase)
                    ? relative["branding/".Length..]
                    : relative;
                target = Path.Combine(ReceiptBrandingStore.DirectoryFor(shop), withinBranding);
            }

            // Zip-slip: an entry name may contain ".." and escape the folder it is meant to land in.
            // The same guard DatabasePathProvider.ExtractPackageSafely applies, for the same reason.
            var fullTarget = Path.GetFullPath(target);
            var allowedRoot = Path.GetFullPath(UserDataPaths.Root);
            if (!fullTarget.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(fullTarget)!);
            entry.ExtractToFile(fullTarget, overwrite: true);
        }
    }

    /// <summary>Per-shop file prefix inside the zip, keyed on PublicId so two shops cannot collide.</summary>
    private static string FileRoot(Shop shop) => $"files/{shop.PublicId:N}/";

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        using var stream = new StreamWriter(archive.CreateEntry(name).Open());
        stream.Write(content);
    }

    private static ShopArchiveSummary Describe(ArchivePayload payload)
        => new(
            payload.Shops?.Count ?? 0,
            payload.Shops?.Sum(shop => shop.Orders?.Count ?? 0) ?? 0,
            payload.ExportedAtUtc,
            payload.Shops?.Select(shop => shop.Shop?.PublicId ?? Guid.Empty).ToList() ?? new List<Guid>());

    private sealed class ArchivePayload
    {
        public int Version { get; set; }
        public DateTime ExportedAtUtc { get; set; }
        public List<ArchivedShop>? Shops { get; set; }
    }

    private sealed class ArchivedShop
    {
        public Shop? Shop { get; set; }
        public List<Order>? Orders { get; set; }
    }
}

/// <summary>What an archive holds — reported before anything is imported.</summary>
public readonly record struct ShopArchiveSummary(
    int Shops, int Orders, DateTime ExportedAtUtc, IReadOnlyList<Guid> PublicIds);

/// <summary>What a restore actually did. <paramref name="Skipped"/> counts shops already present.</summary>
public readonly record struct ShopRestoreResult(int Shops, int Orders, int Skipped);
