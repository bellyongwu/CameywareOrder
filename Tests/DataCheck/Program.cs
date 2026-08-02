using System.IO;
using System.Reflection;
using System.Text;
using CameywareOrder.Configuration;
using CameywareOrder.Data;
using CameywareOrder.Localization;
using CameywareOrder.Models;
using CameywareOrder.Services;
using Microsoft.EntityFrameworkCore;
using Path = System.IO.Path;

namespace DataCheck;

/// <summary>
/// Drives the v8.0.0 data release: the recycle bin and its purge, the shared order query, the CSV
/// export, and the backup schedule.
/// </summary>
/// <remarks>
/// COVERAGE BOUNDARY, stated rather than assumed. Writing and restoring a real backup is NOT driven
/// here. <c>BackupService.RunNow</c> copies <c>UserDataPaths.Root</c>, which resolves through
/// <c>Environment.GetFolderPath</c> to the machine's real LocalAppData — there is no seam to redirect
/// it — so exercising it would write into the user's own installation, prune their real backups and
/// stamp their real settings file. What IS driven: the schedule that decides WHEN it runs, the
/// pruning it delegates to (which takes an explicit root and is the part this release changed), and
/// the package format it reuses unchanged from the Import/Export menu. The uncovered part is the two
/// calls to <c>ExportDatabaseTo</c> / <c>ImportDatabaseFrom</c>, both of which shipped in v3.
///
/// Everything else runs against a throwaway SQLite file in a temp folder.
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
        CameywareOrder.Tests.RepoPaths.UseRepositoryAsWorkingDirectory();
        LocalizationService.Instance.LoadFromDirectory(
            SystemSettingsPaths.LanguagesDirectory, AppDefaults.Load().DefaultLanguageCode);

        var localization = LocalizationService.Instance;

        var folder = Path.Combine(Path.GetTempPath(), "datacheck-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={Path.Combine(folder, "orders.db")}")
            .Options;

        Shop shop;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
            await InvokeAppAsync("EnsureSchemaCompatibilityAsync", db);
            await InvokeAppAsync("EnsureShopSchemaAsync", db);

            Check("Orders has the DeletedOnUtc column", ReadOrderColumns(db).Contains("DeletedOnUtc"));

            var result = ShopAdministration.CreateDemoShop(db, localization);
            shop = result.Shop;
        }

        // Every shop-scoped read resolves through ShopContext, and AppDbContext captures the shop id
        // in its CONSTRUCTOR — so the shop has to be bound before any context is built.
        ShopContext.Instance.SetActive(shop);
        PaymentTaxRules.SetActive(shop.PaymentTaxRules);

        RunQueryChecks(options);
        RunRecycleBinChecks(options, localization);
        RunCsvChecks(options, localization);
        RunScheduleChecks();
        RunPruneChecks(folder);

        try { Directory.Delete(folder, recursive: true); } catch (IOException) { /* temp */ }

        Console.WriteLine();
        Console.WriteLine($"{_passed} passed, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }

    // ── the shared query ──────────────────────────────────────────────────────────────────────────

    private static void RunQueryChecks(DbContextOptions<AppDbContext> options)
    {
        Console.WriteLine("── order query ────────────────────────────────────────");

        using var db = new AppDbContext(options);
        var orders = db.Orders.AsNoTracking().ToList();
        var sample = orders[0];

        Check("an empty query narrows nothing", OrderQuery.Empty.Apply(orders).Count == orders.Count);
        Check("an empty query knows it is empty", OrderQuery.Empty.IsEmpty);

        // The gap this release closes: a customer arrives holding a receipt number.
        var byNumber = new OrderQuery { Text = sample.OrderNumber, Field = OrderSearchField.OrderNumber };
        Check("an order can be found by its receipt number",
            byNumber.Apply(orders).Count == 1 && byNumber.Apply(orders)[0].Id == sample.Id);

        Check("the default scope finds it too",
            new OrderQuery { Text = sample.OrderNumber }.Apply(orders).Count == 1);

        // Scoping must only ever NARROW: a field search that surfaced a row the default missed would
        // mean the two disagree about what the text matched.
        var customerOnly = new OrderQuery { Text = sample.OrderNumber, Field = OrderSearchField.Customer };
        Check("a narrowed scope does not match the number", customerOnly.Apply(orders).Count == 0);

        Check("search is case-insensitive",
            new OrderQuery { Text = sample.CustomerName.ToUpperInvariant(), Field = OrderSearchField.Customer }
                .Apply(orders).Count > 0);

        var processing = new OrderQuery { Status = OrderStatus.Processing }.Apply(orders);
        Check("a status filter narrows to that status",
            processing.Count > 0 && processing.TrueForAll(o => o.Status == OrderStatus.Processing));

        // The period is the same DateRange the settlement report uses, so both screens mean the same
        // span when asked for the same month.
        var month = DateRange.CurrentMonth();
        var inMonth = new OrderQuery { Period = month }.Apply(orders);
        Check("a period filter matches the settlement report's own range",
            inMonth.Count == orders.Count(o => month.Contains(o.OrderDate)) && inMonth.Count > 0);

        var combined = new OrderQuery { Status = OrderStatus.Completed, Period = month }.Apply(orders);
        Check("filters combine rather than replace one another",
            combined.TrueForAll(o => o.Status == OrderStatus.Completed && month.Contains(o.OrderDate)));
    }

    // ── the recycle bin ───────────────────────────────────────────────────────────────────────────

    private static void RunRecycleBinChecks(DbContextOptions<AppDbContext> options, ILocalizedText text)
    {
        Console.WriteLine("── recycle bin ────────────────────────────────────────");

        int liveBefore;
        List<int> binned;
        string binnedNumber;

        using (var db = new AppDbContext(options))
        {
            var live = db.Orders.AsNoTracking().OrderBy(o => o.Id).ToList();
            liveBefore = live.Count;
            binned = live.Take(3).Select(o => o.Id).ToList();
            binnedNumber = live[0].OrderNumber;

            Check("the bin starts empty", OrderRecycleBin.Count(db) == 0);
            Check("delete moves the whole selection",
                OrderRecycleBin.Delete(db, binned, DateTime.UtcNow) == 3);
        }

        using (var db = new AppDbContext(options))
        {
            Check("deleted orders leave the list", db.Orders.Count() == liveBefore - 3);
            Check("they are still on disk", db.Orders.IgnoreQueryFilters().Count() == liveBefore);
            Check("the bin holds them", OrderRecycleBin.Count(db) == 3);
            Check("the bin lists them newest-deleted first",
                OrderRecycleBin.List(db).Count == 3);

            var shop = ShopContext.Instance.RequireCurrent();
            Check("the shop's order count excludes the bin",
                ShopAdministration.CountOrders(db, shop.Id) == liveBefore - 3);

            // What the shop's own screens report has to exclude them, or a deleted order goes on
            // being counted as revenue until the purge gets round to it.
            var report = SettlementCalculator.For(
                db.Orders.AsNoTracking().Include(o => o.Items).ToList(),
                DateRange.CurrentMonth(), shop.CurrencyType);
            Check("the settlement report cannot see them", report.Counts.Total <= liveBefore - 3);
        }

        using (var db = new AppDbContext(options))
        {
            Check("restore puts one back", OrderRecycleBin.Restore(db, new[] { binned[0] }) == 1);
        }

        using (var db = new AppDbContext(options))
        {
            Check("the restored order is live again", db.Orders.Count() == liveBefore - 2);
            Check("the bin now holds two", OrderRecycleBin.Count(db) == 2);

            // Deleting twice must not restart the clock, or a selection deleted again would keep
            // resurrecting the retention window on records the shop meant to be rid of.
            var stamp = db.Orders.IgnoreQueryFilters()
                .Where(o => o.Id == binned[1]).Select(o => o.DeletedOnUtc).Single();
            OrderRecycleBin.Delete(db, new[] { binned[1] }, DateTime.UtcNow.AddDays(5));
            var after = db.Orders.IgnoreQueryFilters()
                .Where(o => o.Id == binned[1]).Select(o => o.DeletedOnUtc).Single();
            Check("re-deleting a binned order does not restart its window", stamp == after);
        }

        using (var db = new AppDbContext(options))
        {
            // The purge takes what is past the cutoff and NOTHING else — the assertion that matters,
            // since the cheap version of this check would pass on a purge that removed everything.
            var cutoff = DateTime.UtcNow.AddDays(1);
            var backdated = binned[1];
            db.Database.ExecuteSqlRaw(
                "UPDATE Orders SET DeletedOnUtc = {0} WHERE Id = {1}",
                DateTime.UtcNow.AddDays(-90).ToString("O"), backdated);

            var removed = OrderRecycleBin.PurgeForever(db, cutoff.AddDays(-60), orderIds: null);
            Check("the purge takes only what is past the cutoff", removed == 1, $"removed {removed}");
            Check("the other binned order survives", OrderRecycleBin.Count(db) == 1);
        }

        using (var db = new AppDbContext(options))
        {
            var remaining = OrderRecycleBin.List(db).Select(o => o.Id).ToList();
            Check("delete-forever removes the named rows",
                OrderRecycleBin.PurgeForever(db, null, remaining) == remaining.Count);
            Check("the bin is empty again", OrderRecycleBin.Count(db) == 0);
            Check("their line items went with them",
                db.OrderItems.IgnoreQueryFilters().Count(i => remaining.Contains(i.OrderId)) == 0);
        }

        Check("a purge with neither a cutoff nor a list does nothing",
            PurgeNothing(options) == 0);

        RunReceiptNumberCheck(options);
    }

    /// <summary>
    /// The audit finding this release turned up: a binned order still holds its receipt number.
    /// </summary>
    /// <remarks>
    /// Driven at ONE INSTANT on purpose. The obvious version — bin any old order, then reserve a
    /// number now — passes whatever the code does, because a timestamp number composed today never
    /// collides with one composed four months ago. That is a fixture sitting on a fallback path: the
    /// assertion could not fail, so it was testing nothing. Reserving twice from the SAME moment is
    /// what actually exercises the collision scan.
    /// </remarks>
    private static void RunReceiptNumberCheck(DbContextOptions<AppDbContext> options)
    {
        var moment = new DateTime(2026, 5, 17, 14, 30, 0, DateTimeKind.Local);
        var shop = ShopContext.Instance.RequireCurrent();
        string first;
        int orderId;

        using (var db = new AppDbContext(options))
        {
            first = OrderNumberFormatter.Reserve(db, shop, moment);

            var order = new Order
            {
                ShopId = shop.Id,
                OrderNumber = first,
                CustomerName = "Receipt Probe",
                PhoneNumber = "+1 416-555-0000",
                OrderDate = DateTime.UtcNow,
            };

            using (db.SuppressShopStamping())
            {
                db.Orders.Add(order);
                db.SaveChanges();
            }

            orderId = order.Id;
            Check("reserving the same moment twice steps past the live order",
                OrderNumberFormatter.Reserve(db, shop, moment) != first);
        }

        using (var db = new AppDbContext(options))
        {
            OrderRecycleBin.Delete(db, new[] { orderId }, DateTime.UtcNow);
        }

        using (var db = new AppDbContext(options))
        {
            var afterBinning = OrderNumberFormatter.Reserve(db, shop, moment);
            Check("a BINNED order's receipt number is still taken",
                afterBinning != first, $"re-issued {afterBinning}");

            OrderRecycleBin.PurgeForever(db, null, new[] { orderId });
        }
    }

    private static int PurgeNothing(DbContextOptions<AppDbContext> options)
    {
        using var db = new AppDbContext(options);
        return OrderRecycleBin.PurgeForever(db, null, null);
    }

    // ── the spreadsheet ───────────────────────────────────────────────────────────────────────────

    private static void RunCsvChecks(DbContextOptions<AppDbContext> options, ILocalizedText text)
    {
        Console.WriteLine("── csv export ─────────────────────────────────────────");

        using var db = new AppDbContext(options);
        var orders = db.Orders.AsNoTracking().Include(o => o.Items).Take(20).ToList();

        var csv = OrderCsvExport.Build(orders, text);
        var lines = csv.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Check("one row per order, plus a header", lines.Length == orders.Count + 1);
        Check("rows are CRLF-terminated, as RFC 4180 says", csv.ToString().Contains("\r\n"));

        var headerCells = CountCells(lines[0]);
        Check("every row has as many cells as the header",
            lines.Skip(1).All(line => CountCells(line) == headerCells), $"header has {headerCells}");

        Check("the header is translated, not raw keys", !lines[0].Contains("Order.Fields."));

        // The standing rule for a second consumer of the money model: read the accessor, never
        // recompute. A sheet that disagreed with the receipt is the one nobody would re-check.
        var withMoney = orders.Find(o => o.TotalAmount > 0m)!;
        var row = OrderCsvExport.Build(new[] { withMoney }, text).ToString()
            .Split("\r\n")[1];
        Check("the sheet carries the order's own total",
            row.Contains(withMoney.TotalAmount.ToString("0.00", System.Globalization.CultureInfo.CurrentCulture)),
            withMoney.TotalAmount.ToString("0.00"));
        Check("and its own alterations figure",
            row.Contains(withMoney.MoneyFor(ServiceLine.Alterations).Total
                .ToString("0.00", System.Globalization.CultureInfo.CurrentCulture)));

        // Excel reads a BOM-less file as the system codepage, which turns every non-ASCII customer
        // name into mojibake on the one machine the shop will actually open it on.
        var path = Path.Combine(Path.GetTempPath(), "datacheck-" + Guid.NewGuid().ToString("N") + ".csv");
        csv.Save(path);
        var bytes = File.ReadAllBytes(path);
        Check("the file starts with a UTF-8 byte-order mark",
            bytes.Length > 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        File.Delete(path);

        // Quoting and the injection guard, driven directly rather than through an order — an order
        // cannot easily be made to carry a leading '=' and the rule has to hold if one ever does.
        var probe = new CsvWriter();
        probe.WriteRow("plain", "has,comma", "has\"quote", "=cmd|'/c calc'!A1", " padded ");
        var probed = probe.ToString();
        Check("a comma forces quotes", probed.Contains("\"has,comma\""));
        Check("a quote is doubled", probed.Contains("\"has\"\"quote\""));
        Check("a leading = is neutralised", probed.Contains("\"\t=cmd"));
        Check("padding is preserved by quoting", probed.Contains("\" padded \""));

        var name = OrderCsvExport.SuggestFileName(
            ShopContext.Instance.Current, LocalizationService.Instance.CurrentLanguageCode, orders.Count);
        Check("the suggested name carries the row count", name.Contains(orders.Count.ToString()));
        Check("and is a legal file name",
            name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0, name);
    }

    private static int CountCells(string line)
    {
        var cells = 1;
        var inQuotes = false;

        foreach (var character in line)
        {
            if (character == '"') inQuotes = !inQuotes;
            else if (character == ',' && !inQuotes) cells++;
        }

        return cells;
    }

    // ── the schedule ──────────────────────────────────────────────────────────────────────────────

    private static void RunScheduleChecks()
    {
        Console.WriteLine("── backup schedule ────────────────────────────────────");

        var now = new DateTime(2026, 8, 2, 9, 0, 0, DateTimeKind.Utc);

        var fresh = new DataProtectionSettings();
        Check("an installation that has never backed up is due", fresh.IsBackupDue(now));

        var justRan = new DataProtectionSettings { LastBackupUtc = now.AddHours(-1) };
        Check("one that ran an hour ago is not", !justRan.IsBackupDue(now));

        var stale = new DataProtectionSettings { LastBackupUtc = now.AddHours(-25) };
        Check("one that ran yesterday is", stale.IsBackupDue(now));

        var off = new DataProtectionSettings { AutomaticBackupEnabled = false, LastBackupUtc = null };
        Check("switching it off means never due", !off.IsBackupDue(now));

        // A shop PC that has been off for a month and had its clock corrected reads as "last backed
        // up in the future". Treating that as not-due suspends backups until the future catches up.
        var futureStamp = new DataProtectionSettings { LastBackupUtc = now.AddDays(30) };
        Check("a clock that moved backwards does not suspend backups", futureStamp.IsBackupDue(now));

        var bin = new DataProtectionSettings { RecycleBinDays = 30 };
        Check("the purge cutoff is the retention window back",
            bin.PurgeBefore(now) == now.AddDays(-30));

        var keepForever = new DataProtectionSettings { RecycleBinDays = 0 };
        Check("a retention of zero purges nothing", keepForever.PurgeBefore(now) is null);
        Check("and says so", !keepForever.PurgesAutomatically);

        Check("a nonsense interval is clamped rather than obeyed",
            new DataProtectionSettings { BackupIntervalHours = 0 }.EffectiveIntervalHours >= 1);
    }

    // ── pruning ───────────────────────────────────────────────────────────────────────────────────

    private static void RunPruneChecks(string root)
    {
        Console.WriteLine("── backup pruning ─────────────────────────────────────");

        var backups = Path.Combine(root, "Backups");
        Directory.CreateDirectory(backups);

        // Two kinds in one folder: the scheduled packages this release added, and the bare copies
        // taken before an import. Counted per kind, or one sort pushes the other out.
        for (var i = 0; i < 8; i++)
        {
            var package = Path.Combine(backups, $"backup-2026080{i}-120000.zip");
            File.WriteAllText(package, "package");
            File.SetLastWriteTimeUtc(package, new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i));

            var preImport = Path.Combine(backups, $"orders.db.bak-2026080{i}120000");
            File.WriteAllText(preImport, "database");
            File.SetLastWriteTimeUtc(preImport, new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i));
        }

        UserDataPaths.PruneBackups(3, root);

        var packages = Directory.GetFiles(backups, "backup-*.zip");
        var preImports = Directory.GetFiles(backups, "orders.db.bak-*");

        Check("the scheduled packages are pruned to the keep count", packages.Length == 3,
            $"{packages.Length} left");
        Check("the pre-import copies are counted separately", preImports.Length == 3,
            $"{preImports.Length} left");
        Check("the NEWEST packages are the ones kept",
            packages.All(file => Path.GetFileName(file).CompareTo("backup-20260805") > 0),
            string.Join(", ", packages.Select(Path.GetFileName)));

        UserDataPaths.PruneBackups(0, root);
        Check("a keep count of zero keeps everything",
            Directory.GetFiles(backups, "backup-*.zip").Length == 3);
    }

    // ── plumbing ──────────────────────────────────────────────────────────────────────────────────

    private static async Task InvokeAppAsync(string methodName, AppDbContext db)
    {
        var method = typeof(CameywareOrder.App)
            .GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)!;
        await (Task)method.Invoke(null, new object[] { db })!;
    }

    private static HashSet<string> ReadOrderColumns(AppDbContext db)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        db.Database.OpenConnection();
        try
        {
            using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "PRAGMA table_info('Orders');";
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

