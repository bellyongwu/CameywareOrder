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
    /// Whether this installation already has its demo store. While it does, the offer to create one
    /// is withdrawn; deleting it brings the offer back.
    /// </summary>
    /// <remarks>
    /// Delisted shops count. A demo store taken out of service still exists, still holds its hundred
    /// orders and can be put back in one click — offering a second one would leave the installation
    /// with two, and the person would have no way to tell which the button had made.
    /// </remarks>
    public static bool HasDemoShop(AppDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Shops.Any(shop => shop.IsDemo);
    }

    /// <summary>
    /// Creates a ready-to-use demo store from the shipped defaults, in one click, seeded with the
    /// preset order history in <c>Settings/System/Defaults/demo-orders.json</c>.
    /// </summary>
    /// <remarks>
    /// The shop's own configuration comes from the same defaults a hand-created shop would be
    /// offered: the home-market jurisdiction, that market's currency and the languages installed on
    /// the system. If the shipped presets change, this changes with them, which is the only version
    /// that cannot drift away from what "default" means everywhere else.
    ///
    /// Two things are deliberately NOT the shipped default, and both are what makes it a DEMO:
    ///
    /// <list type="bullet">
    /// <item>It carries orders. Fabricating customer records used to be the thing this method
    /// refused to do, on the grounds that inventing them inside somebody's real installation is
    /// unwelcome. What changed is that they are now confined to a shop that says on its own row it
    /// is a demo, there is at most one of it, and deleting it takes every one of them with it.</item>
    /// <item>Its tax rules quote a rate. The shipped Canadian and US presets quote none — sales tax
    /// is added at settlement there — so a demo store seeded straight from them shows zero tax
    /// everywhere and demonstrates nothing. See <see cref="DemoOrders.DemonstrationRatePercent"/>.
    /// </item>
    /// </list>
    /// </remarks>
    public static DemoShopResult CreateDemoShop(AppDbContext db, LocalizationService localization)
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
            IsDemo = true,
        };

        // The name in every installed language, so the demo shop reads correctly whichever language the
        // app is switched to afterwards — the same rule a real shop's NamesJson follows.
        shop.SetNames(languages.ToDictionary(
            code => code,
            code => localization.GetText("Store.Demo.Name", code)));

        shop.SetInstalledLanguages(languages);
        shop.SetSupportedCurrencies(new[] { jurisdiction.DefaultCurrency });
        shop.SetPaymentTaxRules(PaymentTaxRules.CreateForStandardRate(DemoOrders.TaxRatePercentFor(shop)));

        db.Shops.Add(shop);
        db.SaveChanges();

        // After the save, because the orders need the shop's Id — and dated from the LOCAL today,
        // which is the day the shop's own people would call today.
        var orders = DemoOrders.Seed(db, shop, DateTime.Today);

        return new DemoShopResult(shop, orders);
    }

    /// <summary>
    /// Duplicates shops: their configuration, their per-shop files, and a name that says which
    /// original each came from. Returns the new rows, in the order the sources were given.
    /// </summary>
    /// <remarks>
    /// A copy is a new BRANCH that starts life configured like an existing one — the same tax rules,
    /// currencies, languages, catalogue, measurement terms and receipt branding. It deliberately
    /// carries NO orders. An order is a record of a transaction that happened at one shop on one day;
    /// duplicating a hundred of them into a second shop would double that branch's revenue in the
    /// settlement report, re-issue receipt numbers already handed to customers, and leave two shops
    /// claiming the same trade. The receipt run therefore restarts at 1 as well.
    ///
    /// Neither <see cref="Shop.IsDemo"/> nor the delisting state travels. A copy is in service on the
    /// day it is made, and it is not a second demo store — see the remarks on that property.
    /// </remarks>
    public static IReadOnlyList<Shop> Copy(
        AppDbContext db, IReadOnlyList<Shop> shops, ILocalizedText localization)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(shops);
        ArgumentNullException.ThrowIfNull(localization);

        if (shops.Count == 0)
            return Array.Empty<Shop>();

        // Every name already in use, in every language, so the numbering cannot collide with a shop
        // the user is not looking at. Grown as copies are added, because two copies of one shop made
        // in a single click would otherwise both be "(copy)".
        // AsEnumerable, not ToList: Shop.Names is [NotMapped] and decoded from JSON, so the SelectMany
        // has to run client-side either way — this one just does not build a second list to throw away.
        var taken = new HashSet<string>(
            db.Shops.AsNoTracking().AsEnumerable().SelectMany(shop => shop.Names.Values),
            StringComparer.OrdinalIgnoreCase);

        var copies = new List<Shop>(shops.Count);

        foreach (var source in shops)
        {
            var copy = BuildCopy(source, CopyNames(source, taken, localization));
            db.Shops.Add(copy);
            copies.Add(copy);

            foreach (var name in copy.Names.Values)
                taken.Add(name);
        }

        db.SaveChanges();

        // After the save: the per-shop files are keyed on PublicId, which is assigned above, but a
        // failed insert must not leave a folder behind for a shop that does not exist.
        for (var index = 0; index < shops.Count; index++)
            CopyPerShopFiles(shops[index], copies[index]);

        return copies;
    }

    /// <summary>Everything a copy inherits from its source, and nothing else — see <see cref="Copy"/>.</summary>
    private static Shop BuildCopy(Shop source, IReadOnlyDictionary<string, string> names)
    {
        var copy = new Shop
        {
            PublicId = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow,
            Code = source.Code,
            AddressesJson = source.AddressesJson,
            PhoneNumber = source.PhoneNumber,
            Email = source.Email,
            Website = source.Website,
            TaxRegistrationNumber = source.TaxRegistrationNumber,
            PreferredLanguageCode = source.PreferredLanguageCode,
            LocationCode = source.LocationCode,
            InstalledLanguagesJson = source.InstalledLanguagesJson,
            CurrencyType = source.CurrencyType,
            SupportedCurrenciesJson = source.SupportedCurrenciesJson,
            PaymentTaxRulesJson = source.PaymentTaxRulesJson,
            OrderNumberMode = source.OrderNumberMode,
            OrderNumberPrefix = source.OrderNumberPrefix,
            OrderNumberPadding = source.OrderNumberPadding,
            // A fresh run, not the source's position in its own. See the remarks on Copy.
            OrderNumberNextSequence = 1,
            OrderNumberSequenceKey = null,
        };

        copy.SetNames(names);
        return copy;
    }

    /// <summary>
    /// The copy's name in every language the source is named in: the source's name plus a localized
    /// "(copy)" suffix, numbered when a plain one is already taken.
    /// </summary>
    /// <remarks>
    /// The number is decided ONCE for the whole shop and applied to every language, rather than per
    /// language. A shop whose English name collides and whose French one does not would otherwise
    /// come out as "Atelier (copy)" in one language and "Atelier (copy 2)" in the next — one shop
    /// telling two stories about which copy it is.
    ///
    /// The suffix itself is a string-table value, not a literal: it is punctuation as much as it is a
    /// word, and Chinese writes the brackets full-width. A shop with no name at all is named by the
    /// suffix alone, which is still recognisable — and better than a blank row.
    /// </remarks>
    private static Dictionary<string, string> CopyNames(
        Shop source, ICollection<string> taken, ILocalizedText localization)
    {
        var names = source.Names.Count > 0
            ? source.Names
            : new Dictionary<string, string> { [source.PreferredLanguageCode ?? string.Empty] = string.Empty };

        for (var index = 0; index < 1_000; index++)
        {
            var suffix = index == 0
                ? localization["Store.Copy.Suffix"]
                : localization.Format("Store.Copy.SuffixNumbered", index);

            var candidate = names.ToDictionary(pair => pair.Key, pair => pair.Value + suffix);
            if (!candidate.Values.Any(taken.Contains))
                return candidate;
        }

        // A thousand copies of one shop is not a real state; falling back to a unique-by-construction
        // name beats looping forever or refusing the copy.
        return names.ToDictionary(
            pair => pair.Key,
            pair => pair.Value + localization.Format("Store.Copy.SuffixNumbered", DateTime.Now.Ticks));
    }

    /// <summary>
    /// The three per-shop files a copy inherits. Each owns its own naming rule and its own
    /// never-overwrite guard, so this only says WHICH of them travel.
    /// </summary>
    private static void CopyPerShopFiles(Shop source, Shop target)
    {
        MeasurementTermsService.CopyConfigBetweenShops(source, target);
        ProductCatalogService.CopyConfigBetweenShops(source, target);
        ReceiptBrandingStore.CopyBrandingBetweenShops(source, target);
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

/// <summary>
/// The demo store and how much history it was given. The count is reported rather than assumed
/// because the preset file can be missing or unreadable, in which case the shop is still created and
/// the panel has to say it arrived empty.
/// </summary>
public readonly record struct DemoShopResult(Shop Shop, int Orders);
