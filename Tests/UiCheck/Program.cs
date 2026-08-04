using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CameywareOrder.Configuration;
using CameywareOrder.Controls;
using CameywareOrder.Data;
using CameywareOrder.Localization;
using CameywareOrder.Models;
using CameywareOrder.Services;
using CameywareOrder.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Path = System.IO.Path;

namespace StoreRender;

/// <summary>
/// Renders Store Management and the shop picker to PNG so the moved buttons can be LOOKED at.
/// </summary>
/// <remarks>
/// Runs against a throwaway SQLite file in a temp folder, never the user's database, and fabricates
/// its own signed-in administrator by reflection rather than authenticating — so `credentials.json`
/// is read (the singleton reads it on first touch) and never written. The hash is checked either
/// side to prove it.
/// </remarks>
internal static class Program
{
    [STAThread]
    private static int Main()
    {
        CameywareOrder.Tests.RepoPaths.UseRepositoryAsWorkingDirectory();
        var outDir = CameywareOrder.Tests.RepoPaths.ScratchDirectory("renders");

        LocalizationService.Instance.LoadFromDirectory(
            SystemSettingsPaths.LanguagesDirectory, AppDefaults.Load().DefaultLanguageCode);

        // Two of the user's own files this run READS and must never write: the accounts and the role
        // definitions. The permission panel is constructed here, and a screen that saved on load
        // would rewrite an installation's whole permission model from a screenshot run.
        var credentials = UserDataPaths.ResolveConfigFile("credentials.json");
        var roles = UserDataPaths.ResolveConfigFile("roles.json");

        // Touched BEFORE the baseline hash, deliberately. The singleton reads credentials.json in its
        // type initializer, and a file written by an older build is UPGRADED and saved on that first
        // read — a legitimate one-time write that any launch of the application would also perform.
        // Hashing first and touching second made a schema bump look like this harness scribbling on
        // the user's accounts. What is being asserted is that the RUN writes nothing, so the baseline
        // is taken once the file is at the current schema.
        _ = AuthenticationService.Instance;

        var before = HashOf(credentials);
        var rolesBefore = HashOf(roles);

        // Default OnLastWindowClose shuts the application down when the FIRST rendered window
        // closes, so every later window comes out blank — which reads as a layout defect.
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/CameywareOrder;component/Themes/AppTheme.xaml")
        });

        var scopeFactory = BuildScopeFactory(out var dbPath);
        SeedShops(scopeFactory);
        SignInAsFabricatedAdministrator();

        var localization = LocalizationService.Instance;

        var management = new StoreManagementWindow(localization, scopeFactory);
        // Shown FIRST: the surface reaches the list through a RelativeSource binding, which does not
        // resolve until the list is loaded into a live tree. Checking straight after the constructor
        // reads as "the binding was never set", which is what it looked like on the first run.
        Show(management, 1020, 880);
        var shortcutsOk = CheckCopyPasteShortcuts(management, scopeFactory, localization);
        Render(management, Path.Combine(outDir, "store-management.png"), 1020, 880);

        var picker = new ShopPickerWindow(localization, scopeFactory,
            AuthenticationService.Instance.CurrentUser, currentShop: null);
        Render(picker, Path.Combine(outDir, "shop-picker.png"), 1000, 740);

        // v8.0 panels. The recycle bin is shop-scoped, so it needs a shop bound; the data-protection
        // panel reads the machine's real settings file (read-only — nothing here saves).
        var shop = FirstShop(scopeFactory);
        CameywareOrder.Services.ShopContext.Instance.SetActive(shop);
        SeedDeletedOrders(scopeFactory, shop);

        var bin = new RecycleBinWindow(localization, scopeFactory);
        Render(bin, Path.Combine(outDir, "recycle-bin.png"), 1000, 720);

        var protection = new DataProtectionWindow(localization);
        Render(protection, Path.Combine(outDir, "data-protection.png"), 960, 760);

        // The redesigned permission panel, with a role selected so all three columns have content.
        var permissions = new PermissionsWindow(localization, scopeFactory);
        Show(permissions, 1180, 800);
        SelectFirst(permissions, "RoleList");
        Render(permissions, Path.Combine(outDir, "permissions.png"), 1180, 800);

        // The sign-in screen in both of its states. The second one is a panel nobody sees until a
        // fresh installation's first launch, which is precisely the screen that must not be the one
        // nobody looked at.
        var login = new LoginWindow(localization);
        Show(login, 440, 600);
        Render(login, Path.Combine(outDir, "login.png"), 440, 600);

        var changing = new LoginWindow(localization);
        Show(changing, 440, 600);
        typeof(LoginWindow)
            .GetMethod("ShowPasswordChangeStep", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(changing, null);
        Render(changing, Path.Combine(outDir, "login-password-change.png"), 440, 600);

        // And again in French, which is the longest of the five for all three of these strings —
        // "Définir le mot de passe et se connecter" on a 376px button. A label clipped mid-word has
        // got through here twice before, both times on a screen that compiled and passed.
        var was = localization.CurrentLanguageCode;
        localization.SetLanguage("fr-FR");
        var french = new LoginWindow(localization);
        Show(french, 440, 600);
        typeof(LoginWindow)
            .GetMethod("ShowPasswordChangeStep", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(french, null);
        Render(french, Path.Combine(outDir, "login-password-change-fr.png"), 440, 600);
        localization.SetLanguage(was);

        // v9.3.0: changing your own password from inside a session. Rendered because the three boxes
        // and the rule line are the kind of thing no assertion can judge, and because French runs a
        // quarter longer than English on every one of these labels.
        var changePassword = new ChangePasswordWindow(localization, AuthenticationService.Instance.CurrentUser!);
        Show(changePassword, 460, 460);
        Render(changePassword, Path.Combine(outDir, "change-password.png"), 460, 460);

        // A copy made through the REAL Copy action, so the list renders what the customer column
        // actually does with the "- Copy 1" suffix rather than a name typed to look like one. The
        // long fixture name is deliberate: the column is 170px with CharacterEllipsis, and a suffix
        // trimmed off the end would leave a copy looking identical to its source.
        SeedLiveOrders(scopeFactory, shop);
        new CameywareOrder.ViewModels.MainViewModel(scopeFactory, localization)
            .CopyOrdersAsync(LiveOrderIds(scopeFactory)).GetAwaiter().GetResult();

        // The main window, for the two-row filter bar the search and export added to it.
        var viewModel = new CameywareOrder.ViewModels.MainViewModel(scopeFactory, localization);
        var main = new CameywareOrder.MainWindow(viewModel, scopeFactory, localization);
        Show(main, 1500, 900);
        shortcutsOk &= CheckLocalConfigMenu(main);
        Render(main, Path.Combine(outDir, "main-filters-closed.png"), 1500, 900);

        // The same window with Advanced search opened, and a date filter set so the "a filter is
        // hiding in here" mark can be seen once it is closed again.
        var opened = new CameywareOrder.MainWindow(
            new CameywareOrder.ViewModels.MainViewModel(scopeFactory, localization),
            scopeFactory, localization);
        Show(opened, 1500, 900);
        Press(opened, "AdvancedSearchButton");
        shortcutsOk &= CheckFilterButtonHeights(opened);
        shortcutsOk &= CheckPeriodBarOnScreen(opened);
        Render(opened, Path.Combine(outDir, "main-filters-open.png"), 1500, 900);

        // The order editor's ready-made section (v9.3.2). Never rendered before, and it is where the
        // column-alignment defect lived: the header row and the item rows are separate Grids, and
        // only the rows carry a Remove button — so the header's trailing Auto column measured zero
        // and every heading drifted right of the values beneath it. Nothing but a picture shows that.
        RenderReadyMadeSection(scopeFactory, localization, Path.Combine(outDir, "order-ready-made.png"));

        // ...and the rule that made that same section unsaveable (v9.3.3): ready-made stock is
        // collected the day it is bought, and the pickup date demanded tomorrow.
        shortcutsOk &= CheckPickupDateFloor(scopeFactory, localization);

        // The period quick-filter (v9.4.0) — a fact about the query, so asserted on the view model.
        shortcutsOk &= CheckPeriodQuickFilter(scopeFactory, localization);

        // The Account menu's CONTENTS (v9.3.1). A Popup lives in its own window and never appears in
        // the parent's RenderTargetBitmap, so opening the menu and re-rendering the window would
        // produce the same picture as before and prove nothing. Its Child is an ordinary visual.
        RenderOpenMenu(opened, "AccountMenuItem", Path.Combine(outDir, "main-account-menu.png"));

        // The busy overlay, held open for the length of the render. Anything with an animation has to
        // be given real time before the shot — a render taken at t=0 catches the bar off-screen.
        var busyModel = new CameywareOrder.ViewModels.MainViewModel(scopeFactory, localization);
        var busyWindow = new CameywareOrder.MainWindow(busyModel, scopeFactory, localization);
        Show(busyWindow, 1500, 900);
        using (busyModel.Busy.Begin(localization["Status.LoadingOrders"]))
        {
            Settle(700);
            Render(busyWindow, Path.Combine(outDir, "main-busy.png"), 1500, 900);
        }

        var after = HashOf(credentials);
        var rolesAfter = HashOf(roles);

        Console.WriteLine(before == after
            ? "credentials.json untouched"
            : "!! credentials.json CHANGED — investigate before trusting this run");

        Console.WriteLine(rolesBefore == rolesAfter
            ? "roles.json untouched"
            : "!! roles.json CHANGED — the permission panel wrote on load");

        shortcutsOk &= before == after && rolesBefore == rolesAfter;

        try { File.Delete(dbPath); } catch (IOException) { /* temp */ }

        Console.WriteLine($"renders written to {outDir}");
        return before == after && shortcutsOk ? 0 : 1;
    }

    /// <summary>
    /// Drives Ctrl+C / Ctrl+V through the COMMANDS the shared binding installs, rather than through
    /// synthesised keystrokes.
    /// </summary>
    /// <remarks>
    /// A fabricated KeyEventArgs would not carry the real Ctrl state — the recorded reason the Ctrl+A
    /// check had to use real device input — so what is asserted here is everything from the attached
    /// property down: that the bindings exist, that CanExecute follows the selection, and that
    /// executing Paste really writes a copy. The gesture-to-command step is WPF's own.
    /// </remarks>
    private static bool CheckCopyPasteShortcuts(
        StoreManagementWindow window, IServiceScopeFactory factory, LocalizationService localization)
    {
        var list = (System.Windows.Controls.ListBox)window.FindName("StoreList")!;
        var ok = true;

        void Check(string what, bool passed)
        {
            Console.WriteLine((passed ? "  PASS  " : "  FAIL  ") + what);
            ok &= passed;
        }

        Check("the list declares a copy/paste surface", CopyPasteBinding.GetSurface(list) is not null);
        Check("Ctrl+C is bound on the list", list.InputBindings.OfType<KeyBinding>().Any(
            b => b.Key == Key.C && b.Modifiers == ModifierKeys.Control && b.Command == ApplicationCommands.Copy));
        Check("Ctrl+V is bound on the list", list.InputBindings.OfType<KeyBinding>().Any(
            b => b.Key == Key.V && b.Modifiers == ModifierKeys.Control && b.Command == ApplicationCommands.Paste));

        AppClipboard.Clear();
        list.UnselectAll();
        Check("Copy is refused with nothing selected", !ApplicationCommands.Copy.CanExecute(null, list));
        Check("Paste is refused with an empty clipboard", !ApplicationCommands.Paste.CanExecute(null, list));

        list.SelectedIndex = 0;
        Check("Copy is offered once a store is selected", ApplicationCommands.Copy.CanExecute(null, list));

        ApplicationCommands.Copy.Execute(null, list);
        Check("Ctrl+C fills the application clipboard", AppClipboard.Holds("Shops"));
        Check("it holds exactly the selection", AppClipboard.Items.Count == 1);

        int before;
        using (var scope = factory.CreateScope())
            before = scope.ServiceProvider.GetRequiredService<AppDbContext>().Shops.Count();

        Check("Paste is offered with shops held", ApplicationCommands.Paste.CanExecute(null, list));
        ApplicationCommands.Paste.Execute(null, list);

        using (var scope = factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Check("Ctrl+V wrote one copy", db.Shops.Count() == before + 1);

            var suffix = localization["Store.Copy.Suffix"];
            Check("the copy is named as one",
                db.Shops.AsNoTracking().AsEnumerable()
                    .Any(shop => shop.ResolveName(localization.CurrentLanguageCode).EndsWith(suffix, StringComparison.Ordinal)));
        }

        // Leave the panel as it was found, so the render below shows the shipped empty state rather
        // than a list with a stray copy in it.
        AppClipboard.Clear();
        list.UnselectAll();

        return ok;
    }

    /// <summary>Puts a window into a live visual tree, off screen, so its bindings resolve.</summary>
    private static void Show(Window window, int width, int height)
    {
        if (window.IsLoaded)
            return;

        window.Width = width;
        window.Height = height;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -10000; // off screen: this is a screenshot, not a session
        window.Top = -10000;
        window.Show();

        window.UpdateLayout();
        Pump();
    }

    private static void Render(Window window, string path, int width, int height)
    {
        Show(window, width, height);

        window.UpdateLayout();
        Pump();

        // Render the window's CONTENT, not the Window. A Window's own Measure asks Win32 for its
        // min/max box and FailFasts the process when it is called outside the normal layout pass —
        // and a NoResize window reports ActualWidth 0 until it has been through one.
        var root = (FrameworkElement)window.Content;
        var pixelWidth = (int)(root.ActualWidth > 0 ? root.ActualWidth : width);
        var pixelHeight = (int)(root.ActualHeight > 0 ? root.ActualHeight : height);

        var target = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        target.Render(root);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using (var stream = File.Create(path))
            encoder.Save(stream);

        window.Close();
        Console.WriteLine($"  {Path.GetFileName(path)}  {pixelWidth}x{pixelHeight}");
    }

    private static void Pump()
    {
        for (var i = 0; i < 6; i++)
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                () => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        }
    }

    private static IServiceScopeFactory BuildScopeFactory(out string dbPath)
    {
        dbPath = Path.Combine(Path.GetTempPath(), "storerender-" + Guid.NewGuid().ToString("N") + ".db");
        var connection = $"Data Source={dbPath}";

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection),
            ServiceLifetime.Transient);

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IServiceScopeFactory>();

        using (var scope = factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.Migrate();
            InvokeApp("EnsureSchemaCompatibilityAsync", db);
            InvokeApp("EnsureShopSchemaAsync", db);
        }

        return factory;
    }

    private static void SeedShops(IServiceScopeFactory factory)
    {
        using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        foreach (var name in new[] { "Yorkville Atelier", "Kensington Workroom", "Markham Fittings" })
        {
            var shop = new Shop { PublicId = Guid.NewGuid(), CreatedAtUtc = DateTime.UtcNow, LocationCode = "CA" };
            shop.SetNames(new Dictionary<string, string> { ["en-US"] = name, ["zh-CN"] = name });
            db.Shops.Add(shop);
        }

        db.SaveChanges();
    }

    /// <summary>
    /// Asserts where Backup &amp; Recovery sits, and that the gating survived being nested.
    /// </summary>
    /// <remarks>
    /// Structure rather than a screenshot: a menu popup lives in its own window and never appears in
    /// the parent's RenderTargetBitmap, so rendering it means opening the popup and rendering
    /// `popup.Child`. What actually matters here is the PARENTING and the visibility rules, and both
    /// are readable straight off the tree.
    /// </remarks>
    private static bool CheckLocalConfigMenu(Window window)
    {
        var ok = true;

        void Check(string what, bool passed)
        {
            Console.WriteLine((passed ? "  PASS  " : "  FAIL  ") + what);
            ok &= passed;
        }

        var localDatabase = (System.Windows.Controls.MenuItem)window.FindName("LocalDatabaseMenuItem")!;
        var protection = (System.Windows.Controls.MenuItem)window.FindName("DataProtectionMenuItem")!;

        Check("Backup & Recovery is inside Local Database",
            localDatabase.Items.Contains(protection));
        Check("and is the first entry in it",
            localDatabase.Items.Count > 0 && ReferenceEquals(localDatabase.Items[0], protection));

        // The administrator holds both capabilities, so everything is visible and the separator earns
        // its place. The interesting case is a role holding only one — asserted through the source in
        // ApplyRolePermissions rather than here, since fabricating a partial role means writing to
        // roles.json, which is the user's own file.
        Check("the parent menu is shown", localDatabase.Visibility == Visibility.Visible);
        Check("Backup & Recovery is shown", protection.Visibility == Visibility.Visible);
        Check("the path tools are shown",
            ((System.Windows.Controls.MenuItem)window.FindName("CopyDataPathMenuItem")!).Visibility
                == Visibility.Visible);
        Check("the separator is shown when both halves are",
            ((System.Windows.Controls.Separator)window.FindName("DataPathToolsSeparator")!).Visibility
                == Visibility.Visible);

        // v9.3.1: Lock / Change Password / Sign Out became one Account menu. Asserted on ORDER as
        // well as membership, because the order is the whole of what was specified and it is the
        // part a later edit would silently disturb.
        var account = (System.Windows.Controls.MenuItem)window.FindName("AccountMenuItem")!;
        var entries = account.Items.OfType<System.Windows.Controls.MenuItem>()
            .Select(item => item.Header?.ToString()).ToList();

        Check("the Account menu holds exactly three entries", entries.Count == 3);
        Check("...Lock first",
            entries.Count > 0 && entries[0] == LocalizationService.Instance["Toolbar.Lock"]);
        Check("...Change Password second",
            entries.Count > 1 && entries[1] == LocalizationService.Instance["Password.Change.Title"]);
        Check("...Sign Out last",
            entries.Count > 2 && entries[2] == LocalizationService.Instance["Toolbar.SignOut"]);

        return ok;
    }

    /// <summary>
    /// Opens the order editor on the ready-made panel with three priced lines and renders that
    /// section alone.
    /// </summary>
    /// <remarks>
    /// The whole window is far taller than a useful screenshot, so this renders the panel rather
    /// than the window — the panel is an ordinary visual inside the same tree, so it needs no
    /// separate measure the way a popup's child does.
    ///
    /// Three rows rather than one: the defect being guarded against is a HEADER that drifts right of
    /// the values, and a single row shows the drift less clearly than a column of them.
    /// </remarks>
    private static void RenderReadyMadeSection(
        IServiceScopeFactory factory, LocalizationService localization, string path)
    {
        var order = new CameywareOrder.Models.Order
        {
            OrderNumber = "ORD-20260801-120000",
            CustomerName = "Layout Probe",
            PhoneNumber = "+1 416-555-0404",
            OrderDate = DateTime.UtcNow,
            ServiceType = CameywareOrder.Models.OrderServiceType.ReadyMade,
            ClothingSubtotal = 520m,
            Items =
            {
                new CameywareOrder.Models.OrderItem { ProductName = "suit-jacket", Quantity = 1, UnitPrice = 80m },
                new CameywareOrder.Models.OrderItem { ProductName = "suit-jacket", Quantity = 1, UnitPrice = 120m },
                new CameywareOrder.Models.OrderItem { ProductName = "trousers", Quantity = 1, UnitPrice = 320m },
            },
        };

        var editor = new CameywareOrder.Views.OrderEditWindow(factory, localization, order);
        Show(editor, 1200, 900);

        // Past the panel's own transition. Animations/PanelTransition runs 0.5s and the panel fades
        // IN from zero opacity, so a render at 400ms came out a blank white rectangle of exactly the
        // right size — the "render after the animation, or the screenshot lies" trap, in the version
        // where the picture is not merely wrong but empty.
        Settle(900);

        if (editor.FindName("ReadyMadePanel") is not FrameworkElement panel || panel.ActualWidth < 1)
        {
            Console.WriteLine("  !! the ready-made panel did not render");
            editor.Close();
            return;
        }

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(panel.ActualWidth), (int)Math.Ceiling(panel.ActualHeight),
            96, 96, PixelFormats.Pbgra32);

        // Through a VisualBrush rather than Render(panel) directly: the transition leaves a
        // TranslateTransform on the panel, and rendering the visual itself draws it at that offset —
        // partly outside the bitmap. A brush painted into a rectangle of the panel's own size
        // neutralises any transform it happens to be carrying. The white backing is because the
        // panel's own background is transparent over the window's page colour.
        var composed = new DrawingVisual();
        using (var context = composed.RenderOpen())
        {
            var bounds = new Rect(0, 0, panel.ActualWidth, panel.ActualHeight);
            context.DrawRectangle(Brushes.White, null, bounds);
            context.DrawRectangle(new VisualBrush(panel) { Stretch = Stretch.None }, null, bounds);
        }

        bitmap.Render(composed);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(path))
            encoder.Save(stream);

        Console.WriteLine($"  {Path.GetFileName(path)}  {bitmap.PixelWidth}x{bitmap.PixelHeight}");
        editor.Close();
    }

    /// <summary>Opens a top-level menu and renders the drop-down itself.</summary>
    /// <remarks>
    /// The popup's <c>Child</c> is what gets rendered, not the window: a <c>Popup</c> is hosted in a
    /// separate HWND and is invisible to the parent's <c>RenderTargetBitmap</c>. The item has to be
    /// realized before its template can be reached, which is what <c>ApplyTemplate</c> is for — the
    /// same null-Template trap that had `uicheck`'s menu check throwing for weeks.
    /// </remarks>
    private static void RenderOpenMenu(Window window, string menuItemName, string path)
    {
        var item = (System.Windows.Controls.MenuItem)window.FindName(menuItemName)!;
        item.IsSubmenuOpen = true;
        Settle(300);

        item.ApplyTemplate();
        if (item.Template.FindName("PART_Popup", item) is not System.Windows.Controls.Primitives.Popup popup
            || popup.Child is not FrameworkElement child)
        {
            Console.WriteLine($"  !! {menuItemName}: no PART_Popup to render");
            return;
        }

        // A popup's child is not laid out by the parent window's pass, so it can still be 0x0 after
        // the dispatcher has been pumped. Measure and arrange it explicitly, or RenderTargetBitmap
        // throws on pixelWidth.
        child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        child.Arrange(new Rect(child.DesiredSize));
        child.UpdateLayout();

        if (child.ActualWidth < 1 || child.ActualHeight < 1)
        {
            Console.WriteLine($"  !! {menuItemName}: the drop-down measured {child.ActualWidth}x{child.ActualHeight}");
            item.IsSubmenuOpen = false;
            return;
        }

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(child.ActualWidth), (int)Math.Ceiling(child.ActualHeight),
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(child);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(path))
            encoder.Save(stream);

        item.IsSubmenuOpen = false;
        Console.WriteLine($"  {Path.GetFileName(path)}  {bitmap.PixelWidth}x{bitmap.PixelHeight}");
    }

    /// <summary>
    /// The two filter buttons carried a local <c>Padding="12,5"</c> against the theme's 16,8, which
    /// left them visibly shorter than every other button in the window (v9.3.1).
    /// </summary>
    /// <remarks>
    /// Driven on a window whose ADVANCED PANEL IS OPEN, not on the default one: Clear filters lives
    /// inside that panel, and a collapsed element measures zero — the check would have compared
    /// 0 against 0 and passed while proving nothing.
    ///
    /// Measured against a real neighbour rather than against the number 33: asserting the literal
    /// height would go red the day somebody legitimately changes the theme's padding, which is the
    /// opposite of what this is for.
    /// </remarks>
    private static bool CheckFilterButtonHeights(Window window)
    {
        var ok = true;

        void Check(string what, bool passed)
        {
            Console.WriteLine((passed ? "  PASS  " : "  FAIL  ") + what);
            ok &= passed;
        }

        var advanced = (System.Windows.Controls.Button)window.FindName("AdvancedSearchButton")!;
        var newOrder = (System.Windows.Controls.Button)window.FindName("NewOrderButton")!;
        var clear = (System.Windows.Controls.Button)window.FindName("ClearFiltersButton")!;

        Check($"the filter buttons are really measured (>0): {advanced.ActualHeight}/{clear.ActualHeight}",
            advanced.ActualHeight > 0 && clear.ActualHeight > 0);
        Check($"Advanced search is as tall as New Order ({advanced.ActualHeight} vs {newOrder.ActualHeight})",
            Math.Abs(advanced.ActualHeight - newOrder.ActualHeight) < 0.5);
        Check($"Clear filters matches it too ({clear.ActualHeight})",
            Math.Abs(clear.ActualHeight - advanced.ActualHeight) < 0.5);

        return ok;
    }

    /// <summary>
    /// The period bar as CHROME: it lives inside Advanced search now, and the forward arrow really
    /// goes grey on the current month (v9.5.0).
    /// </summary>
    /// <remarks>
    /// Everything else about this feature is asserted on the view model, where the query is. These
    /// three are the facts a view model cannot have: that the panel moved, that the buttons are bound
    /// to the commands the assertions exercise rather than to each other, and that WPF's
    /// command-to-<c>IsEnabled</c> path is actually carrying the refusal to the screen.
    ///
    /// Driven on a window whose advanced panel is OPEN, for the same reason the button-height check
    /// is: a collapsed element is not laid out, and these controls are inside it.
    /// </remarks>
    private static bool CheckPeriodBarOnScreen(Window window)
    {
        var ok = true;

        void Check(string what, bool passed)
        {
            Console.WriteLine((passed ? "  PASS  " : "  FAIL  ") + what);
            ok &= passed;
        }

        var panel = (System.Windows.Controls.StackPanel)window.FindName("PeriodFilterPanel")!;
        var back = (System.Windows.Controls.Button)window.FindName("PreviousPeriodButton")!;
        var forward = (System.Windows.Controls.Button)window.FindName("NextPeriodButton")!;
        var year = (System.Windows.Controls.Button)window.FindName("CurrentYearButton")!;

        Check("the period bar is inside the Advanced search panel",
            panel.IsDescendantOf((System.Windows.DependencyObject)window.FindName("AdvancedFilterPanel")!));
        Check($"...and is really laid out there ({panel.ActualWidth}x{panel.ActualHeight})",
            panel.ActualWidth > 0 && panel.ActualHeight > 0);
        Check($"This year is on it and measured ({year.ActualWidth})", year.ActualWidth > 0);
        Check("the back arrow is live on the current month", back.IsEnabled);
        Check("...and the forward arrow is greyed out", !forward.IsEnabled);

        return ok;
    }

    /// <summary>
    /// The period quick-filter: opens on this month, steps both ways, stops at today going forward,
    /// and stays one period with two surfaces (v9.4.0, revised v9.5.0).
    /// </summary>
    /// <remarks>
    /// Driven through the view model rather than the window, because every one of these is a fact
    /// about the QUERY — what the list, the count badge and the CSV export all read. A test that
    /// clicked the arrows would prove the buttons are wired and say nothing about the three consumers
    /// that matter.
    ///
    /// The write-back to the date pickers is asserted explicitly. It is the half that is easy to
    /// leave out — the advanced row already writes INTO the query, so without it the pickers keep
    /// showing whatever they were last given while the list shows another month, and nothing about
    /// the list looks wrong.
    ///
    /// The forward ceiling is asserted on <c>CanExecute</c> AND on the period after an
    /// <c>Execute</c> — a disabled button is a chrome fact, but a command that would still run if
    /// something called it is the bug the chrome was hiding.
    /// </remarks>
    private static bool CheckPeriodQuickFilter(
        IServiceScopeFactory factory, LocalizationService localization)
    {
        var ok = true;

        void Check(string what, bool passed)
        {
            Console.WriteLine((passed ? "  PASS  " : "  FAIL  ") + what);
            ok &= passed;
        }

        var vm = new CameywareOrder.ViewModels.MainViewModel(factory, localization);
        var thisMonth = CameywareOrder.Models.DateRange.CurrentMonth();

        Check("the list opens on the current month",
            vm.Query.Period == thisMonth);
        Check("...and the advanced pickers are seeded from it",
            vm.FromDate == thisMonth.Start && vm.ToDate == thisMonth.LastDay);
        Check("the period reads as the month, not as a span",
            vm.PeriodTitle == thisMonth.Title(localization,
                System.Globalization.CultureInfo.GetCultureInfo(localization.CurrentLanguageCode)));

        vm.PreviousPeriodCommand.Execute(null);
        var lastMonth = thisMonth.Shift(-1);
        Check("the back arrow steps a whole month",
            vm.Query.Period == lastMonth);
        Check("...and drags the pickers with it",
            vm.FromDate == lastMonth.Start && vm.ToDate == lastMonth.LastDay);

        Check("...and forward is available again, because last month is behind us",
            vm.NextPeriodCommand.CanExecute(null));

        // The ceiling (v9.5.0). v9.4.0 deliberately allowed stepping past the current month; a month
        // that has not begun cannot hold an order the shop took, so the arrow stops on this one.
        vm.NextPeriodCommand.Execute(null);
        Check("the forward arrow returns to the current month", vm.Query.Period == thisMonth);
        Check("...and then refuses to go further", !vm.NextPeriodCommand.CanExecute(null));

        vm.NextPeriodCommand.Execute(null);
        Check("...and would not move even if something called it anyway",
            vm.Query.Period == thisMonth);

        Check("backwards is never blocked", vm.PreviousPeriodCommand.CanExecute(null));

        // This year (v9.5.0) — not a wider month: it changes what the ARROWS step by, which is how a
        // shop reaches a year it cannot name in the two pickers without spelling out both ends.
        var thisYear = CameywareOrder.Models.DateRange.CurrentYear();
        vm.CurrentYearCommand.Execute(null);
        Check("This year switches the period to the calendar year", vm.Query.Period == thisYear);
        Check("...and the pickers follow it to both ends of the year",
            vm.FromDate == thisYear.Start && vm.ToDate == thisYear.LastDay);
        Check("...and forward stops on the current year too",
            !vm.NextPeriodCommand.CanExecute(null));

        vm.PreviousPeriodCommand.Execute(null);
        Check("the back arrow now steps a whole YEAR", vm.Query.Period == thisYear.Shift(-1));
        Check("...and forward opens up again", vm.NextPeriodCommand.CanExecute(null));

        vm.CurrentMonthCommand.Execute(null);
        Check("This month comes back from anywhere", vm.Query.Period == thisMonth);

        // Clearing drops the period entirely rather than resetting it to the month: a "clear filters"
        // that left one filter standing would be the button lying about what it did.
        vm.ClearQuery();
        Check("Clear filters clears the period too", vm.Query.Period is null);
        Check("...and the bar says so", vm.PeriodTitle == localization["Filter.Period.All"]);
        Check("...and the query really is empty", !vm.HasQuery);

        // Neither arrow has a period to step from All time. v9.4.0 sent them to the current month on
        // the grounds that a dead button reads as a broken one — but a DISABLED button does not, and
        // an arrow that silently invents a period is the worse of the two.
        Check("neither arrow is live from All time",
            !vm.PreviousPeriodCommand.CanExecute(null) && !vm.NextPeriodCommand.CanExecute(null));
        vm.PreviousPeriodCommand.Execute(null);
        Check("...and calling one anyway does not invent a period", vm.Query.Period is null);

        // A custom span from the advanced row: the arrows must keep working, stepping by its LENGTH.
        vm.FromDate = new DateTime(2026, 3, 10);
        vm.ToDate = new DateTime(2026, 3, 19);
        Check("the advanced pickers still compose a custom span",
            vm.Query.Period?.Kind == CameywareOrder.Models.DatePeriodKind.Custom
            && vm.Query.Period?.DayCount == 10);

        vm.PreviousPeriodCommand.Execute(null);
        Check("the back arrow steps a custom span by its own length",
            vm.Query.Period?.Start == new DateTime(2026, 2, 28)
            && vm.Query.Period?.LastDay == new DateTime(2026, 3, 9));

        // The ceiling reads the span it would LAND on, not the one it is leaving, so a custom range
        // obeys the same one rule as a month and a year without restating it.
        var stepsToToday = 0;
        while (vm.NextPeriodCommand.CanExecute(null) && stepsToToday < 500)
        {
            vm.NextPeriodCommand.Execute(null);
            stepsToToday++;
        }

        Check($"a custom span walks forward and stops on the one containing today ({stepsToToday} steps)",
            vm.Query.Period is { } landed && landed.Start <= DateTime.Today
            && landed.Shift(1).Start > DateTime.Today);

        return ok;
    }

    /// <summary>
    /// The pickup date is floored at the ORDER date, defaults to today, and follows the order date
    /// as it is edited (v9.3.3).
    /// </summary>
    /// <remarks>
    /// Two orders the form could not express before this. The counter sale — ready-made stock handed
    /// over within the hour — has a pickup date of today, and the form demanded tomorrow-or-later.
    /// And a back-dated order could not record the day it was actually collected, because the floor
    /// was today rather than the day the order was taken.
    ///
    /// BOTH ENDS ARE ASSERTED, because they are two halves of one rule and the defect showed up in
    /// each independently: <c>IsPickupDateAllowed</c> is what refuses the save, and the picker's
    /// blackout is what refuses the CLICK. Fixing one and leaving the other is the same bug with a
    /// different symptom — a calendar that strikes out a day the save would have taken.
    ///
    /// The day before the floor is asserted every time. "Today counts" must not quietly become "any
    /// date counts": a test that only proves what is now allowed would pass just as happily against
    /// a check that had been deleted.
    ///
    /// Reflection, because the rule is private and belongs there — it is the window's own answer
    /// about its own fields, not a service anything else should be able to call.
    /// </remarks>
    private static bool CheckPickupDateFloor(
        IServiceScopeFactory factory, LocalizationService localization)
    {
        var ok = true;

        void Check(string what, bool passed)
        {
            Console.WriteLine((passed ? "  PASS  " : "  FAIL  ") + what);
            ok &= passed;
        }

        // A NEW order, deliberately: an order that already carries a date is exempted from the rule,
        // and running this against one would pass no matter what the rule said.
        var editor = new CameywareOrder.Views.OrderEditWindow(factory, localization);
        Show(editor, 1200, 900);

        var pickup = (System.Windows.Controls.DatePicker)editor.FindName("PickupDatePicker")!;
        var orderDate = (System.Windows.Controls.DatePicker)editor.FindName("OrderDatePicker")!;
        var allowed = typeof(CameywareOrder.Views.OrderEditWindow)
            .GetMethod("IsPickupDateAllowed", BindingFlags.Instance | BindingFlags.NonPublic)!;

        Check("a new order defaults its pickup date to today",
            pickup.SelectedDate?.Date == DateTime.Today);

        Check("the calendar offers today", !pickup.BlackoutDates.Contains(DateTime.Today));
        Check("the calendar strikes out the day before the order date",
            pickup.BlackoutDates.Contains(DateTime.Today.AddDays(-1)));

        // The order date moved back a week. The floor has to follow it — this is the back-dated
        // order, where a pickup date in the past is the truth rather than a mistake.
        var backdated = DateTime.Today.AddDays(-7);
        orderDate.SelectedDate = backdated;

        Check("back-dating the order opens the calendar back to that day",
            !pickup.BlackoutDates.Contains(backdated));
        Check("...and no further", pickup.BlackoutDates.Contains(backdated.AddDays(-1)));
        Check("the pickup date already chosen is left alone",
            pickup.SelectedDate?.Date == DateTime.Today);

        // Now forward again, past the chosen pickup date. The stale selection must be snapped to the
        // new floor: a DatePicker THROWS if its SelectedDate is inside BlackoutDates, so leaving it
        // is not one of the options.
        pickup.SelectedDate = backdated;
        orderDate.SelectedDate = DateTime.Today;

        Check("moving the order date past the pickup date snaps the pickup date up to it",
            pickup.SelectedDate?.Date == DateTime.Today);

        // Then the save rule, with the blackout cleared. Not a convenience: assigning SelectedDate a
        // blacked-out day THROWS, so the refused case cannot be reached through a picker that is
        // still refusing it — and the save rule exists precisely for the day the calendar never
        // offered, because the box can be typed into. Clearing it is that typed path.
        pickup.BlackoutDates.Clear();

        bool Accepts(DateTime day)
        {
            pickup.SelectedDate = day;
            return (bool)allowed.Invoke(editor, null)!;
        }

        Check("today is accepted when the order was taken today", Accepts(DateTime.Today));
        Check("tomorrow still is", Accepts(DateTime.Today.AddDays(1)));
        Check("the day before the order date is refused", !Accepts(DateTime.Today.AddDays(-1)));

        editor.Close();
        return ok;
    }

    /// <summary>
    /// Lets real time pass while pumping the dispatcher, so an animation reaches a drawable state.
    /// </summary>
    /// <remarks>
    /// Pumping alone is not enough: an indeterminate progress bar is a storyboard, and a render taken
    /// straight after it starts catches the runner still off the left edge — which reads as a bar that
    /// does not work. The same trap the orders list's fade produced once already.
    /// </remarks>
    private static void Settle(int milliseconds)
    {
        var until = Environment.TickCount64 + milliseconds;

        while (Environment.TickCount64 < until)
        {
            Pump();
            System.Threading.Thread.Sleep(30);
        }
    }

    /// <summary>Selects a list's second row, so a screen renders with content rather than empty.</summary>
    /// <remarks>
    /// The SECOND, not the first: the first role is the administrator, which is deliberately the one
    /// row that cannot be varied — rendering it would show every column in its disabled state.
    /// </remarks>
    private static void SelectFirst(Window window, string listName)
    {
        var list = (System.Windows.Controls.ListBox)window.FindName(listName)!;
        list.SelectedIndex = list.Items.Count > 1 ? 1 : 0;
        window.UpdateLayout();
        Pump();
    }

    /// <summary>Clicks a named button, so a disclosure can be rendered in both of its states.</summary>
    private static void Press(Window window, string name)
    {
        var button = (System.Windows.Controls.Button)window.FindName(name)!;
        button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        window.UpdateLayout();
        Pump();
    }

    private static Shop FirstShop(IServiceScopeFactory factory)
    {
        using var scope = factory.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Shops.AsNoTracking().OrderBy(shop => shop.Id).First();
    }

    /// <summary>Puts a few orders in the bin so the panel renders with rows rather than empty.</summary>
    private static void SeedDeletedOrders(IServiceScopeFactory factory, Shop shop)
    {
        using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var names = new[] { "Amelia Clarke", "Kenji Nakamura", "Rosa Delgado", "Liam Whitfield" };

        for (var index = 0; index < names.Length; index++)
        {
            db.Orders.Add(new Order
            {
                ShopId = shop.Id,
                OrderNumber = $"ORD-20260{7 + index % 2}1{index}-1030{index}0",
                CustomerName = names[index],
                PhoneNumber = "+1 416-555-01" + (10 + index),
                OrderDate = DateTime.UtcNow.AddDays(-20 - index * 9),
                TotalAmount = 180m + index * 145m,
                // Spread across the retention window so both badge colours render: the last one is
                // inside three days of the cutoff and comes out red.
                DeletedOnUtc = DateTime.UtcNow.AddDays(-(index * 9) - (index == 3 ? 28 : 1)),
            });
        }

        using (db.SuppressShopStamping())
            db.SaveChanges();
    }

    /// <summary>Two live orders for the list to render — one ordinary name, one long enough to overflow.</summary>
    private static void SeedLiveOrders(IServiceScopeFactory factory, Shop shop)
    {
        using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var names = new[] { "Priya Raghunathan", "Alexandra Fairweather-Blythe" };

        for (var index = 0; index < names.Length; index++)
        {
            db.Orders.Add(new Order
            {
                ShopId = shop.Id,
                OrderNumber = $"ORD-20260801-1100{index}0",
                CustomerName = names[index],
                PhoneNumber = "+1 416-555-02" + (10 + index),
                OrderDate = DateTime.UtcNow.AddDays(-2 - index),
                ExpectedPickupDate = DateTime.UtcNow.AddDays(6 + index),
                AlterationSubtotal = 240m + index * 90m,
                TotalAmount = 240m + index * 90m,
            });
        }

        using (db.SuppressShopStamping())
            db.SaveChanges();
    }

    private static List<int> LiveOrderIds(IServiceScopeFactory factory)
    {
        using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return db.Orders.AsNoTracking().OrderBy(order => order.Id).Select(order => order.Id).ToList();
    }

    private static void SignInAsFabricatedAdministrator()
    {
        var service = AuthenticationService.Instance;
        var account = new UserAccount("admin", "Ada", "Lovelace", null, null, true,
            Array.Empty<ShopMembership>());

        typeof(AuthenticationService)
            .GetProperty(nameof(AuthenticationService.CurrentUser))!
            .SetValue(service, account);

        typeof(AuthenticationService)
            .GetMethod("RefreshCapabilities", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(service, null);
    }

    private static void InvokeApp(string methodName, AppDbContext db)
    {
        var method = typeof(CameywareOrder.App)
            .GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)!;
        ((Task)method.Invoke(null, new object[] { db })!).GetAwaiter().GetResult();
    }

    private static string HashOf(string path)
    {
        if (!File.Exists(path))
            return "absent";

        using var sha = System.Security.Cryptography.SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream));
    }
}


