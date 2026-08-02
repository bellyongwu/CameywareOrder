using System.IO;
using System.Reflection;
using CameywareOrder.Configuration;
using CameywareOrder.Data;
using CameywareOrder.Localization;
using CameywareOrder.Models;
using CameywareOrder.Services;
using Microsoft.EntityFrameworkCore;
// HotChocolate contributes a global `Path` type; alias it, as the application itself does.
using Path = System.IO.Path;

namespace DemoCheck;

/// <summary>
/// Drives the v7.1.0 store additions against a throwaway database: the demo store and its preset
/// order history, the one-demo-store rule, and Copy Shop's naming.
/// </summary>
/// <remarks>
/// Touches no user data. The database is a fresh file in a temp folder and every shop is created by
/// this run, which is the rule this project's harnesses keep re-learning: a harness must establish
/// what it asserts on.
/// </remarks>
internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static void Check(string what, bool ok, string detail = "")
    {
        if (ok) { _passed++; Console.WriteLine($"  PASS  {what}"); }
        else { _failed++; Console.WriteLine($"  FAIL  {what}   {detail}"); }
    }

    private static async Task<int> Main()
    {
        // The app probes the working directory for Settings/System, so point it at the repo.
        CameywareOrder.Tests.RepoPaths.UseRepositoryAsWorkingDirectory();

        // App.OnStartup loads the string table; a harness never runs that, and every lookup would
        // otherwise return its own key.
        LocalizationService.Instance.LoadFromDirectory(
            SystemSettingsPaths.LanguagesDirectory, AppDefaults.Load().DefaultLanguageCode);

        var localization = LocalizationService.Instance;

        var folder = Path.Combine(Path.GetTempPath(), "democheck-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var dbPath = Path.Combine(folder, "orders.db");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        Console.WriteLine("── schema ─────────────────────────────────────────────");
        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
            await InvokeAppAsync("EnsureSchemaCompatibilityAsync", db);
            await InvokeAppAsync("EnsureShopSchemaAsync", db);

            var columns = ReadShopColumns(db);
            Check("Shops has the IsDemo column", columns.Contains("IsDemo"));

            // Startup repeats the guards on every launch, so a second run must be a no-op rather
            // than a duplicate-column error that bricks the app after one restart.
            var second = true;
            try { await InvokeAppAsync("EnsureShopSchemaAsync", db); }
            catch (Exception ex) { second = false; Console.WriteLine("        " + ex.Message); }
            Check("EnsureShopSchemaAsync is idempotent", second);
        }

        Console.WriteLine("── demo store ─────────────────────────────────────────");
        Shop demo;
        await using (var db = new AppDbContext(options))
        {
            Check("no demo store on a fresh installation", !ShopAdministration.HasDemoShop(db));

            var result = ShopAdministration.CreateDemoShop(db, localization);
            demo = result.Shop;

            Check("the shipped set holds 100 orders", DemoOrders.Count == 100, $"was {DemoOrders.Count}");

            // Date-independent version of the month-to-date check below. A set whose smallest offset
            // is 1 produces an empty month-to-date report on the 1st of every month — the exact shape
            // that shipped once — and a run on any other day would never notice.
            Check("the set includes same-day orders, so month-to-date is never empty",
                DemoOrders.All.Any(t => t.OrderDaysAgo == 0),
                $"smallest offset is {DemoOrders.All.Min(t => t.OrderDaysAgo)}");
            Check("the demo store was seeded with all of them", result.Orders == 100, $"was {result.Orders}");
            Check("the demo store is flagged as one", demo.IsDemo);
            Check("the offer is withdrawn once one exists", ShopAdministration.HasDemoShop(db));
        }

        await using (var db = new AppDbContext(options))
        {
            var orders = db.Orders.IgnoreQueryFilters()
                .Include(o => o.Items)
                .Where(o => o.ShopId == demo.Id)
                .ToList();

            Check("100 orders written", orders.Count == 100, $"was {orders.Count}");
            Check("order numbers are unique",
                orders.Select(o => o.OrderNumber).Distinct().Count() == orders.Count);
            Check("every order carries a customer and a phone",
                orders.TrueForAll(o => !string.IsNullOrWhiteSpace(o.CustomerName)
                                       && !string.IsNullOrWhiteSpace(o.PhoneNumber)));

            // Dates resolve to the seeding day, which is the whole point of storing offsets.
            var newest = orders.Max(o => o.OrderDateLocal);
            var oldest = orders.Min(o => o.OrderDateLocal);
            Check("no order is dated in the future", newest.Date <= DateTime.Today, newest.ToString("u"));
            Check("the newest order is today's", newest.Date == DateTime.Today, newest.ToString("u"));
            Check("the history spans at least four months",
                (DateTime.Today - oldest.Date).TotalDays >= 120, $"{(DateTime.Today - oldest.Date).TotalDays:0} days");

            // The recorded trap: a month-to-date report must not come out empty when the demo store
            // is created on the 1st.
            var monthToDate = DateRange.CurrentMonth();
            var inMonth = orders.Count(o => monthToDate.Contains(o.OrderDate));
            Check("orders land inside the current month-to-date", inMonth > 0, $"{inMonth} of 100");

            Check("every order has a promised pickup day",
                orders.TrueForAll(o => o.ExpectedPickupDate is not null));
            Check("some orders are still open", orders.Exists(o => o.Status == OrderStatus.Processing));
            Check("some orders are finished", orders.Exists(o => o.IsPickedUp));
            Check("some orders were refunded", orders.Exists(o => o.IsRefunded));
            Check("some orders carry measurements", orders.Exists(o => o.HasCustomMadeService));
            Check("some orders carry ready-made lines", orders.Exists(o => o.Items.Count > 0));

            // The money has to be real, or the settlement report demonstrates nothing.
            PaymentTaxRules.SetActive(demo.PaymentTaxRules);
            Check("every order carries a total", orders.TrueForAll(o => o.TotalAmount > 0m),
                $"{orders.Count(o => o.TotalAmount <= 0m)} at zero");
            Check("the stored total matches the recomputed one",
                orders.TrueForAll(o => Math.Abs(o.TotalAmount - o.ComputedSectionsTotal) < 0.005m));
            Check("tax is charged", orders.Sum(o => o.TotalTax) > 0m);

            var report = SettlementCalculator.For(orders, monthToDate, demo.CurrencyType);
            Check("the month-to-date settlement report is not empty", !report.IsEmpty);
            Check("the settlement report shows revenue", report.PostTaxTotal > 0m);
            Check("cash + card + transfer equals what was received",
                Math.Abs((report.CashReceived + report.CardReceived + report.TransferReceived)
                         - report.ReceivedTotal) < 0.005m,
                $"{report.CashReceived + report.CardReceived + report.TransferReceived} vs {report.ReceivedTotal}");
        }

        Console.WriteLine("── copy shop ──────────────────────────────────────────");
        var suffix = localization["Store.Copy.Suffix"];
        var demoName = demo.ResolveName(localization.CurrentLanguageCode);

        await using (var db = new AppDbContext(options))
        {
            var source = db.Shops.AsNoTracking().First(s => s.Id == demo.Id);
            var first = ShopAdministration.Copy(db, new[] { source }, localization).Single();

            Check("the first copy takes the plain suffix",
                first.ResolveName(localization.CurrentLanguageCode) == demoName + suffix,
                first.ResolveName(localization.CurrentLanguageCode));
            Check("the copy is not a second demo store", !first.IsDemo);
            Check("the copy is in service", !first.IsArchived && first.DelistedOnUtc is null);
            Check("the copy starts its own receipt run",
                first.OrderNumberNextSequence == 1 && first.OrderNumberSequenceKey is null);
            Check("the copy keeps the tax rules", first.PaymentTaxRulesJson == source.PaymentTaxRulesJson);
            Check("the copy keeps the currencies",
                first.SupportedCurrenciesJson == source.SupportedCurrenciesJson);
            Check("the copy carries no orders", ShopAdministration.CountOrders(db, first.Id) == 0);
            Check("every language of the copy carries the suffix",
                first.Names.Count == source.Names.Count
                && first.Names.All(pair => pair.Value.EndsWith(suffix, StringComparison.Ordinal)));
        }

        await using (var db = new AppDbContext(options))
        {
            var source = db.Shops.AsNoTracking().First(s => s.Id == demo.Id);
            var second = ShopAdministration.Copy(db, new[] { source }, localization).Single();
            var third = ShopAdministration.Copy(db, new[] { source }, localization).Single();

            Check("a colliding copy is numbered 1",
                second.ResolveName(localization.CurrentLanguageCode)
                    == demoName + localization.Format("Store.Copy.SuffixNumbered", 1),
                second.ResolveName(localization.CurrentLanguageCode));
            Check("the next one is numbered 2",
                third.ResolveName(localization.CurrentLanguageCode)
                    == demoName + localization.Format("Store.Copy.SuffixNumbered", 2),
                third.ResolveName(localization.CurrentLanguageCode));
        }

        await using (var db = new AppDbContext(options))
        {
            // Two copies of one shop in a SINGLE call must not both be "(copy 3)" — the batch has to
            // see its own additions, which is the defect batch Copy Order shipped with once.
            var source = db.Shops.AsNoTracking().First(s => s.Id == demo.Id);
            var pair = ShopAdministration.Copy(db, new[] { source, source }, localization);

            Check("two copies in one click get different names",
                pair[0].ResolveName(localization.CurrentLanguageCode)
                    != pair[1].ResolveName(localization.CurrentLanguageCode),
                string.Join(" / ", pair.Select(s => s.ResolveName(localization.CurrentLanguageCode))));
            Check("still exactly one demo store", db.Shops.Count(s => s.IsDemo) == 1);
        }

        Console.WriteLine("── delete brings the offer back ───────────────────────");
        await using (var db = new AppDbContext(options))
        {
            var stored = db.Shops.AsNoTracking().Where(s => s.IsDemo).ToList();
            ShopAdministration.Delete(db, stored);
            Check("deleting the demo store re-opens the offer", !ShopAdministration.HasDemoShop(db));
            Check("its orders went with it",
                db.Orders.IgnoreQueryFilters().Count(o => o.ShopId == demo.Id) == 0);
        }

        try { Directory.Delete(folder, recursive: true); } catch (IOException) { /* temp folder */ }

        Console.WriteLine();
        Console.WriteLine($"{_passed} passed, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }

    private static async Task InvokeAppAsync(string methodName, AppDbContext db)
    {
        var app = typeof(CameywareOrder.App);
        var method = app.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)
                     ?? throw new MissingMethodException(app.FullName, methodName);

        await (Task)method.Invoke(null, new object[] { db })!;
    }

    private static HashSet<string> ReadShopColumns(AppDbContext db)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        db.Database.OpenConnection();
        try
        {
            using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "PRAGMA table_info('Shops');";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                names.Add(reader.GetString(1));
        }
        finally
        {
            db.Database.CloseConnection();
        }

        return names;
    }
}

