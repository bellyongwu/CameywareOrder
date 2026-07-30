using System.IO;
using CameywareOrder.Data;
using CameywareOrder.Localization;
using CameywareOrder.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CameywareOrder.Services;

/// <summary>
/// The one place the shop-level destructive rules live: delist, put back into service, delete, and
/// reinitialise the installation.
/// </summary>
/// <remarks>
/// Every method here reaches ACROSS shops, which is the opposite of how the rest of the app reads.
/// `AppDbContext` confines `Orders` to the open shop with a query filter, so deleting another shop's
/// orders through a normal query silently does nothing — it matches no rows. Each read of Orders below
/// therefore says <c>IgnoreQueryFilters()</c> explicitly, which is the escape hatch that filter's own
/// comment reserves for exactly this. `Shops` is unfiltered, so it needs no such thing; the asymmetry
/// is worth knowing before adding a method here.
///
/// Deleting a shop also has to reach OUTSIDE the database. A shop owns per-shop files keyed on its
/// <see cref="Shop.PublicId"/> — measurement terms and a branding folder — and orders own attached
/// document images. Removing the rows and leaving the files is a slow leak that eventually hands a
/// NEW shop an old one's settings, because `PublicId` is what those files are named after.
/// </remarks>
public static class ShopAdministration
{
    /// <summary>
    /// Takes a shop out of service without touching a single row of its data. Reversible, and therefore
    /// not gated behind the typed-phrase confirmation that deletion is.
    /// </summary>
    /// <remarks>
    /// Sets the EXISTING <see cref="Shop.IsArchived"/> flag, which the startup shop load, the picker and
    /// the shop-name uniqueness check already honour — so delisting takes effect everywhere without any
    /// of them changing. <see cref="Shop.DelistedOnUtc"/> is stamped beside it as the record of when.
    /// </remarks>
    public static void Delist(AppDbContext db, Shop shop)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(shop);

        if (shop.IsArchived)
            return;

        shop.IsArchived = true;
        shop.DelistedOnUtc = DateTime.UtcNow;
        db.SaveChanges();
    }

    /// <summary>Puts a delisted shop back into service, clearing the flag and the stamp together.</summary>
    public static void Activate(AppDbContext db, Shop shop)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(shop);

        if (!shop.IsArchived)
            return;

        shop.IsArchived = false;
        shop.DelistedOnUtc = null;
        db.SaveChanges();
    }

    /// <summary>How many orders a shop holds — what the panel shows before offering to delete it.</summary>
    /// <remarks>
    /// `IgnoreQueryFilters` because this counts a shop that is NOT the open one, which is every shop in
    /// the list bar at most one. Without it the count reads zero and a delete confirmation would
    /// cheerfully report that there is nothing to lose.
    /// </remarks>
    public static int CountOrders(AppDbContext db, int shopId)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Orders.IgnoreQueryFilters().Count(order => order.ShopId == shopId);
    }

    /// <summary>
    /// Deletes shops and everything that belongs to them: their orders, those orders' items, and the
    /// per-shop files keyed on each shop's <see cref="Shop.PublicId"/>.
    /// </summary>
    /// <remarks>
    /// Items go first and explicitly. The relationship is configured with a cascade, but a cascade only
    /// runs for rows EF is tracking or that SQLite enforces, and these orders are loaded through a
    /// filter-bypassing query in a context scoped to a different shop — so deleting them explicitly is
    /// the version that cannot depend on which of those two happens to apply.
    ///
    /// One `SaveChanges`, so a failure part-way leaves the installation as it was rather than with a
    /// shop whose orders are gone.
    /// </remarks>
    public static ShopDeletionResult Delete(AppDbContext db, IReadOnlyList<Shop> shops)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(shops);

        if (shops.Count == 0)
            return new ShopDeletionResult(0, 0);

        var shopIds = shops.Select(shop => shop.Id).ToList();

        var orders = db.Orders.IgnoreQueryFilters()
            .Include(order => order.Items)
            .Where(order => shopIds.Contains(order.ShopId))
            .ToList();

        foreach (var order in orders)
            db.OrderItems.RemoveRange(order.Items);

        db.Orders.RemoveRange(orders);

        // Re-read the shops through THIS context: the rows handed in may have been loaded by another
        // scope, and RemoveRange on an untracked instance throws rather than deleting.
        var tracked = db.Shops.Where(shop => shopIds.Contains(shop.Id)).ToList();
        db.Shops.RemoveRange(tracked);

        db.SaveChanges();

        foreach (var shop in shops)
            DeletePerShopFiles(shop);

        return new ShopDeletionResult(tracked.Count, orders.Count);
    }

    /// <summary>
    /// Returns the installation to its just-installed state as far as SHOP data goes: every shop, every
    /// order, and every per-shop file.
    /// </summary>
    /// <remarks>
    /// Accounts, the saved language and the global settings are deliberately KEPT. Wiping
    /// `credentials.json` would reset the administrator's own password to the seeded default, i.e. the
    /// person running the reset is the person it locks out of their own installation. The next launch
    /// finds no shops and takes the existing sign-in to the create-first-shop path, which is a real
    /// supported state rather than a new one invented for this feature.
    /// </remarks>
    public static ShopDeletionResult Reinitialize(AppDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        return Delete(db, db.Shops.ToList());
    }

    /// <summary>
    /// Creates a ready-to-use shop from the shipped defaults, in one click, with no form to fill in.
    /// </summary>
    /// <remarks>
    /// Everything comes from the same defaults a hand-created shop would be offered: the home-market
    /// jurisdiction, that market's currency, payment-tax rules seeded from its standard rate, and the
    /// languages installed on the system. Nothing here is a second set of defaults invented for demo
    /// use — if the shipped presets change, this changes with them, which is the only version that
    /// cannot drift away from what "default" means everywhere else.
    ///
    /// No orders are fabricated. A demo shop is somewhere to start working; inventing customer records
    /// inside somebody's real installation is a different and much less welcome thing.
    /// </remarks>
    public static Shop CreateDemoShop(AppDbContext db, LocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(localization);

        var jurisdiction = TaxJurisdictions.Default;
        var languages = localization.AvailableLanguages.Select(language => language.Code).ToList();

        var shop = new Shop
        {
            PublicId = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow,
            LocationCode = jurisdiction.Code,
            CurrencyType = jurisdiction.DefaultCurrency,
            PreferredLanguageCode = localization.CurrentLanguageCode,
        };

        // The name in every installed language, so the demo shop reads correctly whichever language the
        // app is switched to afterwards — the same rule a real shop's NamesJson follows.
        shop.SetNames(languages.ToDictionary(
            code => code,
            code => localization.GetText("Store.Demo.Name", code)));

        shop.SetInstalledLanguages(languages);
        shop.SetSupportedCurrencies(new[] { jurisdiction.DefaultCurrency });
        shop.SetPaymentTaxRules(PaymentTaxRules.CreateForStandardRate(jurisdiction.StandardRatePercent));

        db.Shops.Add(shop);
        db.SaveChanges();

        return shop;
    }

    /// <summary>
    /// Every shop, delisted ones included, newest last. Store Management is the one screen that must
    /// see a delisted shop — everywhere else it is meant to be invisible.
    /// </summary>
    public static List<Shop> AllShops(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        using var scope = scopeFactory.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Shops.AsNoTracking().OrderBy(shop => shop.Id).ToList();
    }

    /// <summary>
    /// The measurement-terms file and branding folder a shop owns, both named after its
    /// <see cref="Shop.PublicId"/>. Best-effort: a file already gone is the state this wanted, and a
    /// locked one must not abort a deletion whose rows are already committed.
    /// </summary>
    private static void DeletePerShopFiles(Shop shop)
    {
        TryDeleteFile(MeasurementTermsService.FilePathFor(shop));
        TryDeleteDirectory(ReceiptBrandingStore.DirectoryFor(shop));
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left behind rather than failing the delete. See the remarks above.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // As above.
        }
    }
}

/// <summary>What a deletion actually removed — reported back so the panel can say so.</summary>
public readonly record struct ShopDeletionResult(int Shops, int Orders);
