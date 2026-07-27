using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Data.Common;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using CameywareOrder.Data;
using CameywareOrder.GraphQL;
using CameywareOrder.Localization;
using CameywareOrder.Models;
using CameywareOrder.Services;
using CameywareOrder.ViewModels;
using CameywareOrder.Views;

namespace CameywareOrder;

public partial class App : Application
{
    private const string ServerScheme = "http";
    private const string ServerHost = "localhost";

    /// <summary>
    /// Where the GraphQL endpoint is published when the port is free. It is only a PREFERENCE: a
    /// second instance (or any other process holding 5050) sends the server to an ephemeral port
    /// instead of failing, so the well-known address belongs to whoever starts first.
    /// </summary>
    private const int PreferredServerPort = 5050;

    /// <summary>
    /// Port 0 asks the OS for any free port. Used to DISCOVER a fallback port number, which is then
    /// bound explicitly — see <see cref="ResolveServerPort"/>.
    /// </summary>
    private const int AnyFreePort = 0;

    /// <summary>
    /// The address the GraphQL endpoint actually ended up on, or <c>null</c> when it could not be
    /// started at all. Nothing in the desktop app reads through it — it is an integration surface
    /// for external callers — which is precisely why a failure here must not stop the app.
    /// </summary>
    internal static string? ApiEndpoint { get; private set; }

    private IHost? _host;
    private readonly LanguagePreferenceStore _languagePreferenceStore = new();
    // Set while a shop's own preferred language is being applied. The global preference file backs
    // the pre-shop screens (login, shop picker); without this guard, opening a zh-CN shop would
    // rewrite it and the login screen's language would silently become whatever shop was opened last.
    private bool _suppressGlobalLanguageSave;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // OnStartup can only be async void, so any exception thrown past the first await would
        // become an unhandled dispatcher exception and the app would simply vanish with no
        // message. Startup does real work — schema migration, file I/O — so failures are
        // reported and the app exits deliberately with a non-zero code.
        try
        {
            await StartApplicationAsync();
        }
        catch (Exception ex)
        {
            // Deliberately not localized: localization is itself part of startup and may be
            // exactly what failed, so this cannot depend on the string table being loaded.
            MessageBox.Show(ex.ToString(), "Cameyware Order — startup failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private async Task StartApplicationAsync()
    {
        // Prevent WPF from shutting down when the language picker closes.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // FIRST, before anything resolves a storage path. The product rename moved the data folder
        // from %LocalAppData%\LeeYongeOrdering to \CameywareOrder, and EnsureDatabasePathReady
        // below CREATES that folder — so running this any later would find the destination already
        // present, skip the move, and hand an existing shop an empty order list.
        LocalDataFolderMigration.EnsureCurrentFolderName();

        var localization = LocalizationService.Instance;
        var languageFilePath = ResolveLanguageFilePath();
        localization.LoadFromFile(languageFilePath);

        DatabasePathProvider.EnsureDatabasePathReady();

        var savedLanguageCode = LanguagePreferenceStore.TryLoadLanguageCode();
        if (!string.IsNullOrWhiteSpace(savedLanguageCode))
            localization.SetLanguage(savedLanguageCode);

        localization.LanguageChanged += (_, _) =>
        {
            if (_suppressGlobalLanguageSave)
                return;

            _languagePreferenceStore.SaveLanguageCode(localization.CurrentLanguageCode);
        };

        // Sign in first. This replaces the standalone language picker, which became redundant once
        // each shop carried its own preferred language; the login window hosts the language
        // selector so a fresh install can still be switched before signing in.
        var loginWindow = new LoginWindow(localization);
        if (loginWindow.ShowDialog() is not true)
        {
            // ShutdownMode is OnExplicitShutdown until the main window appears, so simply
            // returning here would leave a running process with no window — invisible in the task
            // bar and holding the database open. Exit deliberately.
            Shutdown();
            return;
        }

        // Resolved BEFORE the host is built, because the URL is baked in at build time: retrying a
        // different port after a failed StartAsync would mean rebuilding the whole container.
        var serverPort = ResolveServerPort();

        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug(); // visible in VS Output window only
            })
            .ConfigureWebHostDefaults(web =>
            {
                web.UseUrls($"{ServerScheme}://{ServerHost}:{serverPort}");
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapGraphQL());
                });
            })
            .ConfigureServices(services =>
            {
                // SQLite – data stored at <AppDir>/orders.db
                services.AddDbContext<AppDbContext>(options =>
                    options.UseSqlite(DatabasePathProvider.ConnectionString));

                services.AddSingleton(localization);

                // Hot Chocolate GraphQL schema
                services
                    .AddGraphQLServer()
                    .AddQueryType<Query>()
                    .AddMutationType<Mutation>()
                    .AddFiltering()
                    .AddSorting()
                    .RegisterDbContext<AppDbContext>();

                // WPF services
                services.AddTransient<MainViewModel>();
                services.AddTransient<MainWindow>();
            })
            .Build();

        // Bring the schema up to date. THE ORDER OF THESE THREE CALLS IS LOAD-BEARING:
        //
        //   1. Baseline first — marks a pre-existing database (one created before migrations
        //      existed) as already having InitialCreate applied, so step 2 does not try to
        //      re-create tables that are already there. It no-ops on a fresh database.
        //   2. Migrate second — on a FRESH database this is what actually creates Orders and
        //      OrderItems. The two migrations between them cover 18 columns.
        //   3. Column guards last — they add the ~38 further Orders columns the model has
        //      gained since, and they can only see the table once step 2 has created it.
        //
        // Running the guards FIRST (as this used to) silently skipped every one of them on a
        // fresh database, because they bail when Orders does not exist yet — leaving a table
        // with 18 of the model's ~50 columns and a "no such column" crash on the first query.
        // A fresh install was broken outright. Do not reorder these.
        using (var scope = _host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await EnsureMigrationBaselineAsync(db);
            await db.Database.MigrateAsync();
            await EnsureSchemaCompatibilityAsync(db);
            await EnsureShopSchemaAsync(db);
            await EnsureShopBootstrapAsync(db, localization);
        }

        // Open a shop before anything shop-scoped can run.
        ShopContext.Instance.Initialize(_host.Services.GetRequiredService<IServiceScopeFactory>());

        var shopSelection = await OpenInitialShopAsync();
        if (!shopSelection.Opened)
        {
            // No shop was opened — the user cancelled the picker, or there is nothing they may
            // open. Same reasoning as the login window: ShutdownMode is still OnExplicitShutdown,
            // so returning would leave a windowless process holding the database.
            Shutdown();
            return;
        }

        // Start Kestrel + all hosted services. Deliberately non-fatal — see the method.
        await StartApiServerAsync();

        ShowMainWindow(shopSelection.ConfigureTerms);
    }

    /// <summary>
    /// Ends the session and runs sign-in again, without restarting the process. The generic host
    /// and Kestrel keep running throughout: they are not session state, and tearing them down would
    /// mean rebuilding the whole DI container to hand the next user an identical one.
    /// </summary>
    internal async Task SignOutAsync()
    {
        // ORDER MATTERS. ShutdownMode has to be relaxed BEFORE the main window closes, or WPF
        // treats that close as the end of the application and the login window never appears.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var previousWindow = MainWindow;
        MainWindow = null;
        previousWindow?.Close();

        // After the window is gone, never before: every capability gate reads CurrentUser, and
        // revoking it under a live window would leave admin-only controls on screen.
        AuthenticationService.Instance.SignOut();

        var localization = LocalizationService.Instance;

        // The user name is deliberately NOT pre-filled here. Signing out is overwhelmingly "let
        // somebody else take over", and a pre-filled name invites typing the next person's
        // password against the previous person's account.
        var loginWindow = new LoginWindow(localization, seedDefaultUserName: false);
        if (loginWindow.ShowDialog() is not true)
        {
            Shutdown();
            return;
        }

        // The previous session's shop stays bound until the next one is chosen. Clearing it here
        // would open a window in which the GraphQL server — still running — hits RequireCurrent and
        // throws. It is never observable: the only way past this point without choosing a shop is
        // the shutdown below.
        var shopSelection = await OpenInitialShopAsync();
        if (!shopSelection.Opened)
        {
            Shutdown();
            return;
        }

        ShowMainWindow(shopSelection.ConfigureTerms);
    }

    /// <summary>
    /// Starts Kestrel and every hosted service, treating a failure to bind as a DEGRADED START
    /// rather than a fatal one.
    /// </summary>
    /// <remarks>
    /// This used to be a bare <c>await _host.StartAsync()</c>, and a busy port ended the whole
    /// application: the exception reached OnStartup's catch, which reports and calls Shutdown(1).
    /// The user saw "Failed to bind to address http://127.0.0.1:5050: address already in use" while
    /// signing in, and could not read a single order.
    ///
    /// That was the wrong trade. Nothing in the desktop app talks to this endpoint — the UI reads
    /// and writes through <see cref="AppDbContext"/> directly — so the GraphQL server is an
    /// integration surface for EXTERNAL callers. Losing it costs those callers a connection; losing
    /// the app costs the shop its orders. Only the first of those is acceptable.
    ///
    /// The port is already resolved to a free one by <see cref="ResolveServerPort"/>, so reaching
    /// the catch takes a genuine race (something claimed the port between the probe and the bind)
    /// or a machine policy that forbids listening at all. Both leave the app fully usable.
    /// </remarks>
    private async Task StartApiServerAsync()
    {
        var logger = _host!.Services.GetRequiredService<ILogger<App>>();

        try
        {
            await _host.StartAsync();
            ApiEndpoint = ReadBoundAddress(_host);

            if (ApiEndpoint is not null)
                logger.LogInformation("GraphQL endpoint listening on {Endpoint}.", ApiEndpoint);
        }
        catch (System.IO.IOException ex)
        {
            // Only IOException: that is what Kestrel wraps a bind failure in. A broader catch would
            // swallow a genuinely broken hosted service and start the app in a state nobody checked.
            ApiEndpoint = null;
            logger.LogWarning(ex, "The GraphQL endpoint could not be started; continuing without it. "
                + "The desktop application does not depend on it.");
        }
    }

    /// <summary>
    /// Returns <see cref="PreferredServerPort"/> when it is free, otherwise a concrete free port.
    /// </summary>
    /// <remarks>
    /// The overwhelmingly common cause of a busy 5050 is a second copy of this app — another
    /// instance, or one left running with no window. Falling back means the second instance still
    /// gets a working API instead of being refused a start, and the first keeps the well-known
    /// address.
    ///
    /// A CONCRETE port is resolved here rather than handing Kestrel port 0, because "localhost:0"
    /// makes it resolve one hostname to two loopback addresses and take a separate ephemeral port
    /// for each. Picking the number first means the fallback binds exactly the way the 5050 path
    /// does, with one address to report.
    ///
    /// Both probes are advisory, not guarantees: a port can be claimed in the gap before Kestrel
    /// binds it, which is why <see cref="StartApiServerAsync"/> still handles the failure.
    /// </remarks>
    private static int ResolveServerPort()
    {
        if (IsPortAvailable(PreferredServerPort))
            return PreferredServerPort;

        // When even this fails the machine is refusing to let us listen at all. Return the
        // preferred port and let the guarded start report it — there is no port that would work.
        return TryFindFreePort() ?? PreferredServerPort;
    }

    private static bool IsPortAvailable(int port)
    {
        try
        {
            using var probe = new TcpListener(IPAddress.Loopback, port);
            probe.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static int? TryFindFreePort()
    {
        try
        {
            using var probe = new TcpListener(IPAddress.Loopback, AnyFreePort);
            probe.Start();
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        catch (SocketException)
        {
            return null;
        }
    }

    /// <summary>
    /// The address Kestrel actually bound. Read back rather than reconstructed, because with
    /// <see cref="AnyFreePort"/> the real port is only known after the bind succeeds.
    /// </summary>
    private static string? ReadBoundAddress(IHost host)
        => host.Services.GetService<IServer>()?.Features.Get<IServerAddressesFeature>()?
            .Addresses.FirstOrDefault();

    private void ShowMainWindow(bool configureTerms)
    {
        // Resolved fresh each time. MainViewModel is transient and the window caches the signed-in
        // user's capabilities at construction, so reusing an instance across sign-ins would show
        // the new user the previous user's toolbar.
        var mainWindow = _host!.Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        mainWindow.Show();

        // Deferred until the shop is both open and bound: the terms editor writes to whichever
        // shop MeasurementTermsService is pointed at, so offering it any earlier would have
        // configured the shop the administrator was leaving.
        if (configureTerms)
            new MeasurementTermsWindow { Owner = mainWindow }.ShowDialog();
    }

    private static string ResolveLanguageFilePath()
    {
        var inAppDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "Languages.xml");
        if (System.IO.File.Exists(inAppDirectory))
            return inAppDirectory;

        var inWorkingDirectory = System.IO.Path.Combine(Environment.CurrentDirectory, "Languages.xml");
        if (System.IO.File.Exists(inWorkingDirectory))
            return inWorkingDirectory;

        throw new System.IO.FileNotFoundException("Languages.xml was not found.");
    }

    /// <summary>
    /// Idempotent column guards: each entry is applied only when the column is absent, so new
    /// model properties reach an existing shop database without a migration.
    /// </summary>
    /// <remarks>
    /// DO NOT run <c>dotnet ef migrations add</c> to replace these. Migrations/AppDbContextModelSnapshot.cs
    /// is stale — it records 22 Order properties against the model's ~50 — so a scaffolded
    /// migration would emit AddColumn for ~28 columns that already exist in every live database,
    /// and the next MigrateAsync would fail with "duplicate column name" on every installation.
    /// Add a new entry to this table instead. If migrations are ever to be adopted properly, the
    /// snapshot has to be regenerated against the real model first, as its own dedicated task.
    /// </remarks>
    private static readonly (string Column, string Ddl)[] OrderColumnMigrations =
    {
        ("PhoneNumber", "ALTER TABLE Orders ADD COLUMN PhoneNumber TEXT NOT NULL DEFAULT ''; "),
        ("Email", "ALTER TABLE Orders ADD COLUMN Email TEXT NULL; "),
        ("Address", "ALTER TABLE Orders ADD COLUMN Address TEXT NULL; "),
        ("CurrencyType", "ALTER TABLE Orders ADD COLUMN CurrencyType INTEGER NOT NULL DEFAULT 1; "),
        ("ServiceType", "ALTER TABLE Orders ADD COLUMN ServiceType INTEGER NOT NULL DEFAULT 1; "),
        ("ServiceDetails", "ALTER TABLE Orders ADD COLUMN ServiceDetails TEXT NULL; "),
        ("AdditionalNotes", "ALTER TABLE Orders ADD COLUMN AdditionalNotes TEXT NULL; "),
        ("Subtotal", "ALTER TABLE Orders ADD COLUMN Subtotal TEXT NULL; "),
        ("TaxRate", "ALTER TABLE Orders ADD COLUMN TaxRate TEXT NULL; "),
        ("AlterationSubtotal", "ALTER TABLE Orders ADD COLUMN AlterationSubtotal TEXT NULL; "),
        ("AlterationTaxRate", "ALTER TABLE Orders ADD COLUMN AlterationTaxRate TEXT NULL; "),
        ("ClothingSubtotal", "ALTER TABLE Orders ADD COLUMN ClothingSubtotal TEXT NULL; "),
        ("ClothingTaxRate", "ALTER TABLE Orders ADD COLUMN ClothingTaxRate TEXT NULL; "),
        ("CustomMadeTaxRate", "ALTER TABLE Orders ADD COLUMN CustomMadeTaxRate TEXT NULL; "),
        // Per-stage tax rates: the XxxTaxRate columns above hold the deposit-stage rate and
        // these hold the final-balance-stage rate. Null on an existing row means the order
        // predates the split, so its single stored rate keeps applying to both portions.
        ("AlterationFinalTaxRate", "ALTER TABLE Orders ADD COLUMN AlterationFinalTaxRate TEXT NULL; "),
        ("ClothingFinalTaxRate", "ALTER TABLE Orders ADD COLUMN ClothingFinalTaxRate TEXT NULL; "),
        ("CustomMadeFinalTaxRate", "ALTER TABLE Orders ADD COLUMN CustomMadeFinalTaxRate TEXT NULL; "),
        // Owning shop. Non-null with a 0 default so the backfill has an unambiguous "not yet
        // assigned" marker; EnsureShopBootstrapAsync claims every 0 row for the first shop.
        ("ShopId", "ALTER TABLE Orders ADD COLUMN ShopId INTEGER NOT NULL DEFAULT 0; "),
        ("ChestSize", "ALTER TABLE Orders ADD COLUMN ChestSize TEXT NULL; "),
        ("JacketLength", "ALTER TABLE Orders ADD COLUMN JacketLength TEXT NULL; "),
        ("CustomMadeRecordsJson", "ALTER TABLE Orders ADD COLUMN CustomMadeRecordsJson TEXT NULL; "),
        ("AlterationDownpayment", "ALTER TABLE Orders ADD COLUMN AlterationDownpayment TEXT NULL; "),
        ("AlterationDownpaymentMethod", "ALTER TABLE Orders ADD COLUMN AlterationDownpaymentMethod INTEGER NULL; "),
        ("AlterationDownpaymentCompleted", "ALTER TABLE Orders ADD COLUMN AlterationDownpaymentCompleted INTEGER NOT NULL DEFAULT 0; "),
        ("AlterationFinalBalanceMethod", "ALTER TABLE Orders ADD COLUMN AlterationFinalBalanceMethod INTEGER NULL; "),
        ("AlterationBalanceCleared", "ALTER TABLE Orders ADD COLUMN AlterationBalanceCleared INTEGER NOT NULL DEFAULT 0; "),
        ("CustomMadeDownpayment", "ALTER TABLE Orders ADD COLUMN CustomMadeDownpayment TEXT NULL; "),
        ("CustomMadeDownpaymentMethod", "ALTER TABLE Orders ADD COLUMN CustomMadeDownpaymentMethod INTEGER NULL; "),
        ("CustomMadeDownpaymentCompleted", "ALTER TABLE Orders ADD COLUMN CustomMadeDownpaymentCompleted INTEGER NOT NULL DEFAULT 0; "),
        ("CustomMadeFinalBalanceMethod", "ALTER TABLE Orders ADD COLUMN CustomMadeFinalBalanceMethod INTEGER NULL; "),
        ("CustomMadeBalanceCleared", "ALTER TABLE Orders ADD COLUMN CustomMadeBalanceCleared INTEGER NOT NULL DEFAULT 0; "),
        ("ClothingDownpayment", "ALTER TABLE Orders ADD COLUMN ClothingDownpayment TEXT NULL; "),
        ("ClothingDownpaymentMethod", "ALTER TABLE Orders ADD COLUMN ClothingDownpaymentMethod INTEGER NULL; "),
        ("ClothingDownpaymentCompleted", "ALTER TABLE Orders ADD COLUMN ClothingDownpaymentCompleted INTEGER NOT NULL DEFAULT 0; "),
        ("ClothingFinalBalanceMethod", "ALTER TABLE Orders ADD COLUMN ClothingFinalBalanceMethod INTEGER NULL; "),
        ("ClothingBalanceCleared", "ALTER TABLE Orders ADD COLUMN ClothingBalanceCleared INTEGER NOT NULL DEFAULT 0; "),
        ("LastModifiedDate", "ALTER TABLE Orders ADD COLUMN LastModifiedDate TEXT NULL; "),
        ("StatusReason", "ALTER TABLE Orders ADD COLUMN StatusReason TEXT NULL; "),
        ("StatusReasonCategory", "ALTER TABLE Orders ADD COLUMN StatusReasonCategory TEXT NULL; "),
    };

    /// <summary>
    /// Columns added to Shops after that table first shipped. The CREATE TABLE guard in
    /// <see cref="EnsureShopSchemaAsync"/> covers a fresh install only — an existing database
    /// already has the table, so IF NOT EXISTS does nothing there and each later column needs its
    /// own ALTER. Keep the two lists in step: a column added to one and not the other works on
    /// exactly one kind of installation.
    /// </summary>
    private static readonly (string Column, string Ddl)[] ShopColumnMigrations =
    {
        ("PaymentTaxRulesJson", "ALTER TABLE Shops ADD COLUMN PaymentTaxRulesJson TEXT NULL; "),
        ("OrderNumberMode", "ALTER TABLE Shops ADD COLUMN OrderNumberMode INTEGER NOT NULL DEFAULT 0; "),
        ("OrderNumberPrefix", "ALTER TABLE Shops ADD COLUMN OrderNumberPrefix TEXT NULL; "),
        ("OrderNumberPadding", "ALTER TABLE Shops ADD COLUMN OrderNumberPadding INTEGER NOT NULL DEFAULT 4; "),
        ("OrderNumberNextSequence", "ALTER TABLE Shops ADD COLUMN OrderNumberNextSequence INTEGER NOT NULL DEFAULT 1; "),
        ("OrderNumberSequenceKey", "ALTER TABLE Shops ADD COLUMN OrderNumberSequenceKey TEXT NULL; "),
    };

    /// <summary>
    /// Adds every Orders/OrderItems column the model has gained since the last real migration.
    /// MUST run AFTER <c>MigrateAsync</c> — see the ordering comment at the call site. The
    /// HasOrdersTable check below is now only a defensive no-op; if it ever fires it means the
    /// migration step failed to create the table, and skipping quietly beats a raw SQL error.
    /// </summary>
    private static async Task EnsureSchemaCompatibilityAsync(AppDbContext db)
    {
        var schema = await ReadOrdersSchemaAsync(db);
        if (!schema.HasOrdersTable)
            return;

        foreach (var (column, ddl) in OrderColumnMigrations)
        {
            if (!schema.OrderColumns.Contains(column))
                await db.Database.ExecuteSqlRawAsync(ddl);
        }

        if (schema.HasOrderItemsTable && !schema.OrderItemColumns.Contains("PromotionalPrice"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE OrderItems ADD COLUMN PromotionalPrice TEXT NULL; ");

        // Legacy status normalization:
        // old Pending(0) is now mapped to Processing(1).
        await db.Database.ExecuteSqlRawAsync("UPDATE Orders SET Status = 1 WHERE Status = 0;");
    }

    /// <summary>
    /// Selects the shop to work in and applies its settings. Shop-scoped services are bound here,
    /// before any of them is read, so none of them can ever resolve against "no shop".
    /// </summary>
    /// <remarks>
    /// Reports whether a shop was opened rather than letting the caller test
    /// <c>ShopContext.HasShop</c>: on the sign-out path the PREVIOUS session's shop is still bound,
    /// so that test would read true after a cancelled picker and drop the new user into the old
    /// user's shop.
    /// </remarks>
    private async Task<ShopSelection> OpenInitialShopAsync()
    {
        var shops = await LoadSelectableShopsAsync();

        // Staff and managers on a single-shop installation — the overwhelmingly common case — get
        // no picker at all. A modal with exactly one choice is a keystroke tax on every shift.
        // Administrators always see it: choosing and managing shops is what it is for.
        if (shops.Count == 1 && !AuthenticationService.Instance.CanManageShops)
        {
            ApplyActiveShop(shops[0]);
            return ShopSelection.Success();
        }

        // Only reachable when every shop has been archived — the bootstrap always creates one on a
        // fresh database. An administrator can create a shop from the picker; nobody else can, so
        // there is nothing to show them but an explanation.
        if (shops.Count == 0 && !AuthenticationService.Instance.CanManageShops)
        {
            var localization = LocalizationService.Instance;
            MessageBox.Show(
                localization["Shop.Picker.NoShopForRole"],
                localization["Shop.Picker.Title"],
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return ShopSelection.Cancelled;
        }

        var picker = new ShopPickerWindow(
            LocalizationService.Instance,
            _host!.Services.GetRequiredService<IServiceScopeFactory>(),
            AuthenticationService.Instance.CurrentUser,
            // Null at startup; on the sign-out path this preselects whatever was open, which is
            // usually where the next person is working too.
            currentShop: ShopContext.Instance.Current);

        if (picker.ShowDialog() is not true || picker.SelectedShop is null)
            return ShopSelection.Cancelled;

        ApplyActiveShop(picker.SelectedShop);
        return ShopSelection.Success(picker.ConfigureTermsRequested);
    }

    /// <summary>Outcome of choosing a shop: whether one was opened, and what to do next.</summary>
    private readonly record struct ShopSelection(bool Opened, bool ConfigureTerms)
    {
        public static ShopSelection Cancelled => new(false, false);

        public static ShopSelection Success(bool configureTerms = false) => new(true, configureTerms);
    }

    private async Task<List<Shop>> LoadSelectableShopsAsync()
    {
        using var scope = _host!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Shops
            .AsNoTracking()
            .Where(s => !s.IsArchived)
            .OrderBy(s => s.Id)
            .ToListAsync();
    }

    /// <summary>
    /// Opens a shop from outside startup — the 切换店铺 command, and 店铺设置 after an edit.
    /// Deliberately routes through the same method startup uses, so a shop opened mid-session and
    /// one opened at launch can never end up in different states.
    /// </summary>
    internal void OpenShop(Shop shop) => ApplyActiveShop(shop);

    /// <summary>
    /// Makes <paramref name="shop"/> the active one and re-points every shop-scoped service at it.
    /// </summary>
    private void ApplyActiveShop(Shop shop)
    {
        // For a user allowed to choose, the language on screen is the session's language — whether
        // they picked it on the login screen or simply accepted what was shown. Overriding it with
        // the shop's preference made the displayed value a lie: the screen said English, the app
        // then opened in Chinese. It also matters on a switch: an administrator working across
        // branches should not have the UI language change under them every time they move shop.
        //
        // Everyone else runs in the language their shop is configured for, so a branch's staff all
        // see the same thing. The login picker itself stays usable for everyone, or a user could
        // not read the screen they sign in on.
        var keepCurrentLanguage = AuthenticationService.Instance.CanChooseLanguage;
        ShopContext.Instance.SetActive(shop);

        // The shop's own language wins once it is open — UNLESS the user just picked one by hand
        // on the login screen. An explicit choice beats a stored default; overriding it a second
        // later reads as the app ignoring the user. Suppress the global-preference save while
        // applying it: that file is what the pre-shop screens use, and letting a shop overwrite it
        // would make the login screen's language a side effect of whichever shop was opened last.
        if (!keepCurrentLanguage && !string.IsNullOrWhiteSpace(shop.PreferredLanguageCode))
        {
            _suppressGlobalLanguageSave = true;
            try
            {
                LocalizationService.Instance.SetLanguage(shop.PreferredLanguageCode);
            }
            finally
            {
                _suppressGlobalLanguageSave = false;
            }
        }

        CurrencySettingService.Instance.BindTo(shop);
        MeasurementTermsService.Instance.BindTo(shop);

        // The shop's tax rules become the ones every money calculation reads. Bound here rather
        // than looked up per call because Order.CalculateSectionPayment is static and runs on both
        // the UI and Kestrel threads; one assignment per shop switch keeps a single answer to
        // "is this payment method taxed" everywhere.
        PaymentTaxRules.SetActive(shop.PaymentTaxRules);
    }

    /// <summary>
    /// Creates the Shops table and the order-to-shop index. Hand-written DDL rather than an EF
    /// migration for the reason documented on <see cref="OrderColumnMigrations"/> — the model
    /// snapshot is stale, so scaffolding a migration would break every existing installation.
    /// The column list here MUST stay in step with the Shop mapping in
    /// <c>AppDbContext.OnModelCreating</c>: EF 8 does no model-vs-database check, so a mismatch
    /// only shows up as a runtime failure when a row is read.
    /// </summary>
    private static async Task EnsureShopSchemaAsync(AppDbContext db)
    {
        // SQLite type affinities EF expects: int/bool/enum -> INTEGER, string/Guid/DateTime -> TEXT.
        //
        // NamesJson deliberately has NO column default. ExecuteSqlRawAsync treats the SQL as a
        // composite format string, so a literal '{}' in the DDL is parsed as a malformed
        // parameter placeholder and throws FormatException ("expected an ASCII digit") before a
        // single statement runs. The Shop.NamesJson property already defaults to an empty JSON
        // object, so every insert supplies a value and the NOT NULL constraint is satisfied.
        await db.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS Shops (
                Id INTEGER NOT NULL CONSTRAINT PK_Shops PRIMARY KEY AUTOINCREMENT,
                PublicId TEXT NOT NULL,
                Code TEXT NULL,
                NamesJson TEXT NOT NULL,
                PreferredLanguageCode TEXT NULL,
                CurrencyType INTEGER NOT NULL DEFAULT 1,
                CreatedAtUtc TEXT NOT NULL,
                IsArchived INTEGER NOT NULL DEFAULT 0,
                PaymentTaxRulesJson TEXT NULL,
                OrderNumberMode INTEGER NOT NULL DEFAULT 0,
                OrderNumberPrefix TEXT NULL,
                OrderNumberPadding INTEGER NOT NULL DEFAULT 4,
                OrderNumberNextSequence INTEGER NOT NULL DEFAULT 1,
                OrderNumberSequenceKey TEXT NULL
            );");

        // A database created by an earlier build already HAS the table, so CREATE TABLE IF NOT
        // EXISTS above does nothing for it — every column added to Shops after the table shipped
        // needs its own guard here, exactly like OrderColumnMigrations does for Orders.
        var shopColumns = await ReadShopColumnsAsync(db);
        foreach (var (column, ddl) in ShopColumnMigrations)
        {
            if (!shopColumns.Contains(column))
                await db.Database.ExecuteSqlRawAsync(ddl);
        }

        await db.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_Shops_PublicId ON Shops (PublicId);");

        // Not part of OrderColumnMigrations: that table is keyed on column existence, and an index
        // is not a column. It must also run after the ShopId column guard above has applied.
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_Orders_ShopId ON Orders (ShopId);");
    }

    /// <summary>
    /// Adopts whatever is already on this machine as the first shop, and claims every unassigned
    /// order for it. Idempotent: once any shop exists this does nothing, so a second launch cannot
    /// create a duplicate. Seeds the name from the existing <c>Main.HeaderTitle</c> string in every
    /// installed language, so the shop reads correctly in both zh-CN and en-US from day one.
    /// </summary>
    private static async Task EnsureShopBootstrapAsync(AppDbContext db, LocalizationService localization)
    {
        var existing = await db.Shops.OrderBy(s => s.Id).FirstOrDefaultAsync();
        if (existing is not null)
        {
            // A shop already exists, so nothing is created. The adoption still runs because a
            // database bootstrapped by an earlier build predates it; both calls no-op once that
            // shop has files of its own. Restricted to the LOWEST-ID shop so a branch created
            // later never inherits this machine's original configuration.
            AdoptLegacyConfigFor(existing);
            await ClaimUnassignedOrdersAsync(db, existing.Id);
            return;
        }

        var names = localization.AvailableLanguages
            .ToDictionary(
                language => language.Code,
                language => localization.GetText("Main.HeaderTitle", language.Code));

        var shop = new Shop
        {
            PublicId = Guid.NewGuid(),
            PreferredLanguageCode = localization.CurrentLanguageCode,
            CurrencyType = CurrencySettingService.Instance.Current,
            CreatedAtUtc = DateTime.UtcNow
        };
        shop.SetNames(names);

        db.Shops.Add(shop);
        await db.SaveChangesAsync();

        await ClaimUnassignedOrdersAsync(db, shop.Id);
        AdoptLegacyConfigFor(shop);
    }

    /// <summary>
    /// Gives every order with no owner to <paramref name="shopId"/>. Runs on EVERY launch, not
    /// just at bootstrap: an order saved before ShopId was stamped centrally lands at 0 and would
    /// otherwise be invisible once the list is filtered by shop. Zero can only mean "written
    /// before stamping existed", so the first shop is the only sensible owner. A no-op once
    /// SaveChangesAsync stamps every new order, which makes it a permanent safety net rather than
    /// a migration step.
    /// </summary>
    private static async Task ClaimUnassignedOrdersAsync(AppDbContext db, int shopId)
    {
        // Parameterised, never interpolated (SonarQube S2077).
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE Orders SET ShopId = {0} WHERE ShopId = 0;", shopId);
    }

    /// <summary>
    /// Hands this machine's pre-multi-shop measurement terms and branding to the first shop, so
    /// nothing it had configured is lost. Both calls copy rather than move — the originals stay as
    /// a rollback safety net — and both no-op once the shop has files of its own, so this is safe
    /// to run on every launch. Only ever called for the first shop: a branch created later seeds
    /// defaults or copies from a shop the user picks.
    /// </summary>
    private static void AdoptLegacyConfigFor(Shop shop)
    {
        MeasurementTermsService.AdoptLegacyFileFor(shop);
        ReceiptBrandingStore.AdoptLegacyFolderFor(shop);
    }

    private static async Task<(bool HasOrdersTable, bool HasOrderItemsTable, HashSet<string> OrderColumns, HashSet<string> OrderItemColumns)> ReadOrdersSchemaAsync(AppDbContext db)
    {
        var orderColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var orderItemColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool hasOrdersTable;
        bool hasOrderItemsTable;

        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            hasOrdersTable = await TableExistsAsync(connection, "Orders");
            hasOrderItemsTable = await TableExistsAsync(connection, "OrderItems");

            if (hasOrdersTable)
                await ReadColumnNamesAsync(connection, "PRAGMA table_info('Orders');", orderColumns);

            if (hasOrderItemsTable)
                await ReadColumnNamesAsync(connection, "PRAGMA table_info('OrderItems');", orderItemColumns);
        }
        finally
        {
            await connection.CloseAsync();
        }

        return (hasOrdersTable, hasOrderItemsTable, orderColumns, orderItemColumns);
    }

    private static async Task<HashSet<string>> ReadShopColumnsAsync(AppDbContext db)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            await ReadColumnNamesAsync(connection, "PRAGMA table_info('Shops');", columns);
        }
        finally
        {
            await connection.CloseAsync();
        }

        return columns;
    }

    private static async Task<bool> TableExistsAsync(DbConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@name";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
    }

    private static async Task ReadColumnNamesAsync(DbConnection connection, string pragmaSql, HashSet<string> columns)
    {
        // PRAGMA table_info: cid, name, type, notnull, dflt_value, pk
        using var command = connection.CreateCommand();
        command.CommandText = pragmaSql;
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            columns.Add(reader.GetString(1));
    }

    private static async Task EnsureMigrationBaselineAsync(AppDbContext db)
    {
        bool hasOrdersTable = false;
        bool hasHistoryTable = false;

        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            using (var ordersCheckCommand = connection.CreateCommand())
            {
                ordersCheckCommand.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Orders';";
                hasOrdersTable = Convert.ToInt32(await ordersCheckCommand.ExecuteScalarAsync()) > 0;
            }

            if (!hasOrdersTable)
                return;

            using (var historyCheckCommand = connection.CreateCommand())
            {
                historyCheckCommand.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '__EFMigrationsHistory';";
                hasHistoryTable = Convert.ToInt32(await historyCheckCommand.ExecuteScalarAsync()) > 0;
            }

            if (!hasHistoryTable)
            {
                await db.Database.ExecuteSqlRawAsync(@"
                    CREATE TABLE IF NOT EXISTS __EFMigrationsHistory (
                        MigrationId TEXT NOT NULL CONSTRAINT PK___EFMigrationsHistory PRIMARY KEY,
                        ProductVersion TEXT NOT NULL
                    );");
            }

            // Mark the existing schema as the initial migration baseline.
            await db.Database.ExecuteSqlRawAsync(@"
                INSERT OR IGNORE INTO __EFMigrationsHistory (MigrationId, ProductVersion)
                VALUES ('20260723015334_InitialCreate', '7.0.20');");
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(3));
            _host.Dispose();
        }
        base.OnExit(e);
    }
}
