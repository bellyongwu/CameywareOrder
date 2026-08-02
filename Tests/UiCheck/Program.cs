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
        Render(opened, Path.Combine(outDir, "main-filters-open.png"), 1500, 900);

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


