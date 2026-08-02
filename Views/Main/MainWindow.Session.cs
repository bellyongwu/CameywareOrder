using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Diagnostics.CodeAnalysis;
using CameywareOrder.Controls;
using CameywareOrder.Data;
using CameywareOrder.Localization;
using CameywareOrder.Models;
using CameywareOrder.Services;
using CameywareOrder.ViewModels;
using CameywareOrder.Views;

using Microsoft.EntityFrameworkCore;

namespace CameywareOrder;

public partial class MainWindow
{
    // Who is signed in and which shop is open: the greeting, the language scope, locking, signing out, switching shop and signing in as somebody else. Every capability gate the chrome applies is here too.

    /// <summary>
    /// Applies the signed-in user's capabilities to the chrome. Kept in one place so a new
    /// role rule has a single obvious home rather than being scattered through the handlers.
    /// </summary>
    /// <remarks>
    /// Re-run on every shop switch, not only at construction: the same person can be a manager in
    /// one branch and staff in the next, so a menu that was correct when the window opened is not
    /// necessarily correct after Switch Shop. Everything here is hidden rather than disabled — a dead
    /// control invites a support call, an absent one reads as "not offered".
    /// </remarks>
    private void ApplyRolePermissions()
    {
        var auth = AuthenticationService.Instance;

        RefreshLanguageScope();

        // One capability per screen, rather than one "configures the shop" flag covering four of
        // them: an installation that wants somebody editing the product list but not the tax rate
        // can now say so, and each menu item names the permission that governs it.
        ShopSettingsMenuItem.Visibility = Show(auth.CanConfigureShop);
        MeasurementTermsMenuItem.Visibility = Show(auth.CanManageMeasurementTerms);
        ProductCatalogMenuItem.Visibility = Show(auth.CanManageProductCatalog);
        HeaderFooterMenuItem.Visibility = Show(auth.CanManageBranding);
        SettlementMenuItem.Visibility = Show(auth.CanViewReports);
        RecycleBinMenuItem.Visibility = Show(auth.CanManageRecycleBin);

        // Whole-installation tools, and the database path they act on.
        //
        // Local Database now holds two DIFFERENTLY gated things: the path tools (CanUseDataTools) and
        // Backup & Recovery (CanManageBackups). So the parent opens when either is held and each
        // child answers for itself — gating the parent on the path tools alone would hide the backup
        // panel from precisely the person granted the capability to restore one.
        var dataTools = Show(auth.CanUseDataTools);
        DataProtectionMenuItem.Visibility = Show(auth.CanManageBackups);
        CopyDataPathMenuItem.Visibility = dataTools;
        RevealDataFileMenuItem.Visibility = dataTools;
        OpenDataFolderMenuItem.Visibility = dataTools;

        // The separator only earns its place with something on both sides of it.
        DataPathToolsSeparator.Visibility = Show(auth.CanUseDataTools && auth.CanManageBackups);
        LocalDatabaseMenuItem.Visibility = Show(auth.CanUseDataTools || auth.CanManageBackups);
        ImportExportMenuItem.Visibility = dataTools;
        DataPathSeparator.Visibility = dataTools;
        DataPathLabelItem.Visibility = dataTools;
        DataPathValueItem.Visibility = dataTools;

        UserManagementMenuItem.Visibility = Show(auth.CanManageUsers);
        PermissionsMenuItem.Visibility = Show(auth.CanManagePermissions);
        StoreMembersButton.Visibility = Show(auth.CanManageStoreMembers);

        // Hidden when everything below it is: a separator with nothing under it reads as a menu
        // that failed to load.
        ConfigSeparator.Visibility = Show(
            auth.CanConfigureShop || auth.CanManageMeasurementTerms || auth.CanManageProductCatalog
            || auth.CanViewReports || auth.CanUseDataTools || auth.CanPrintOrderDocuments
            || auth.CanManageRecycleBin || auth.CanManageBackups);

        // Exporting the whole customer list in one file is a different act from reading it on screen,
        // so it carries its own capability rather than riding on ViewOrders.
        ExportCsvButton.Visibility = Show(auth.CanExportOrders);

        RefreshOrderActions();

        // Re-asked here as well as on every reload: a shop switch can change whether this user may
        // read reports at all, and no order reload necessarily follows it.
        RefreshSummaryStrip();
        RefreshSignedInUser();
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private void OnShopChanged(object? sender, EventArgs e) => ApplyRolePermissions();

    private void RefreshSignedInUser()
    {
        var auth = AuthenticationService.Instance;
        var user = auth.CurrentUser;

        if (user is null)
        {
            GreetingText.Text = string.Empty;
            return;
        }

        // Greeted by FIRST NAME where there is one — a greeting says "Hi Tina", not "Hi Tina Zhang"
        // and certainly not "Hi tina.zhang". Falls back through the full name to the login, so a
        // person with no name recorded is still addressed as something. The role shown is the one
        // held in the OPEN shop, because that is the one the surrounding chrome has just been gated
        // by; the tooltip carries the login, which is the fact a support call actually needs.
        var role = UserPresentation.RoleList(_localization, auth.CurrentRoles());
        GreetingText.Text = _localization.Format("Main.Greeting", user.GreetingName, role);
        GreetingText.ToolTip = _localization.Format("Shop.Picker.SignedInAs", user.UserName, role);
    }

    /// <summary>
    /// The Lock button, and ESC: offers the choice, then carries it out.
    /// </summary>
    /// <remarks>
    /// One entry point for both, so the toolbar button and the key can never drift into meaning
    /// different things. The open-editor guard runs FIRST, before the panel appears: an order editor
    /// left open behind a locked screen still holds its record and its window, which is most of what
    /// locking is supposed to prevent — and refusing after the user has already chosen would be
    /// asking a question whose answer is then thrown away.
    /// </remarks>
    private async void OnLockClick(object sender, RoutedEventArgs e) => await OfferSessionChoiceAsync();

    private void OfferSessionChoice() => _ = OfferSessionChoiceAsync();

    private async Task OfferSessionChoiceAsync()
    {
        if (!EnsureNoOpenOrderWindows("SignOut.CloseEditors", "Session.Action.Title"))
            return;

        var panel = new SessionActionWindow(
            _localization,
            AuthenticationService.Instance.CurrentUser,
            ShopContext.Instance.Current?.ResolveName(_localization.CurrentLanguageCode))
        {
            Owner = this,
        };

        panel.ShowDialog();

        switch (panel.Action)
        {
            case SessionAction.Lock:
                await RunSessionChangeAsync(() => ((App)Application.Current).LockAsync());
                break;
            case SessionAction.SignOut:
                await RunSessionChangeAsync(() => ((App)Application.Current).SignOutAsync());
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// Runs a sign-out or lock, reporting a failure the user can act on rather than losing it.
    /// </summary>
    /// <remarks>
    /// These are reached from <c>async void</c> handlers and take the main window down as their first
    /// act, so an exception would otherwise reach the dispatcher with no window left to show it
    /// against — the application would simply vanish. No owner is passed for the same reason: by the
    /// time this runs there may not be one.
    /// </remarks>
    private async Task RunSessionChangeAsync(Func<Task> change)
    {
        try
        {
            await change();
        }
        catch (Exception ex)
        {
            // Localized, unlike the startup and data-folder failures, because by here the string
            // table is loaded. The exception text sits below a plain-language line: a stack trace
            // alone tells the person nothing about whether they are still signed in.
            MessageBox.Show(
                $"{_localization["SignOut.Failed"]}{Environment.NewLine}{Environment.NewLine}{ex}",
                _localization["App.MainTitle"],
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void OnSignOutClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureNoOpenOrderWindows("SignOut.CloseEditors", "Toolbar.SignOut"))
            return;

        var answer = MessageBox.Show(
            this,
            _localization["SignOut.Confirm"],
            _localization["Toolbar.SignOut"],
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            await ((App)Application.Current).SignOutAsync();
        }
        catch (Exception ex)
        {
            // This handler is async void and the window it belongs to is already closing, so an
            // exception here would otherwise take the whole dispatcher down with no explanation.
            // No owner window: by this point there may not be one.
            //
            // Localized, unlike the startup and data-folder failures, because by here the string
            // table is loaded — those two run before it and say so in their own comments. The
            // exception text is kept below a plain-language line: a stack trace alone tells the
            // person nothing about whether they are still signed in.
            MessageBox.Show(
                $"{_localization["SignOut.Failed"]}{Environment.NewLine}{Environment.NewLine}{ex}",
                _localization["App.MainTitle"],
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Points the language toggle at the languages this session may actually pick from, and states
    /// under the greeting which ones the open shop runs in.
    /// </summary>
    /// <remarks>
    /// Re-run on every shop switch, like the rest of <see cref="ApplyRolePermissions"/>: the set is
    /// a property of the SHOP for everyone but an administrator, so a toggle that was right when
    /// the window opened is not necessarily right after Switch Shop — a manager may move from a
    /// bilingual branch to one that runs in a single language.
    ///
    /// Hidden outright at one language rather than shown disabled. A picker holding a single option
    /// is chrome that cannot do anything, and the Auto grid column collapses with it, so the bar
    /// leaves no gap for the users who never see it.
    /// </remarks>
    private void RefreshLanguageScope()
    {
        var shop = ShopContext.Instance.Current;
        var selectable = ShopLanguages.Selectable(
            shop, AuthenticationService.Instance.CanChooseAnyLanguage, _localization);

        // Assigning ItemsSource raises SelectionChanged, which would otherwise re-apply whatever
        // landed in the box as a deliberate language choice.
        _isLanguageSwitchInitializing = true;
        try
        {
            LanguageSwitchBox.ItemsSource = selectable;
            LanguageSwitchBox.DisplayMemberPath = nameof(LanguageOption.Name);
            LanguageSwitchBox.SelectedValuePath = nameof(LanguageOption.Code);
            LanguageSwitchBox.SelectedValue = _localization.CurrentLanguageCode;
        }
        finally
        {
            _isLanguageSwitchInitializing = false;
        }

        var toggle = Show(selectable.Count > 1);
        LanguageSwitchLabel.Visibility = toggle;
        LanguageSwitchBox.Visibility = toggle;

        RefreshInstalledLanguagesText();
    }

    /// <summary>
    /// States which languages the open shop runs in, under the greeting.
    /// </summary>
    /// <remarks>
    /// Describes the SHOP, never the administrator's wider choice: "which languages is this branch
    /// set up for" is the useful fact, and it is the one an administrator standing in the branch
    /// wants too. Separate from <see cref="RefreshLanguageScope"/> because a language switch changes
    /// this line's wording while leaving the toggle's contents alone — each language names itself in
    /// its own file, so the options do not need rebuilding.
    /// </remarks>
    private void RefreshInstalledLanguagesText()
    {
        var shop = ShopContext.Instance.Current;

        // Nothing to say before a shop is open.
        InstalledLanguagesText.Visibility = Show(shop is not null);
        InstalledLanguagesText.Text = shop is null
            ? string.Empty
            : ShopLanguages.InstalledSummary(shop, _localization);
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLanguageSwitchInitializing)
            return;

        if (LanguageSwitchBox.SelectedValue is string selectedCode)
            _localization.SetLanguage(selectedCode);
    }

    private void OnLanguageChangedGlobally(object? sender, EventArgs e)
    {
        if (_suppressLanguageRefresh)
            return;

        _suppressLanguageRefresh = true;
        try
        {
            LanguageSwitchBox.SelectedValue = _localization.CurrentLanguageCode;
            _viewModel.StatusMessage = _localization["Status.Ready"];
            DataContext = null;
            DataContext = _viewModel;
            RefreshToolbarLabels();

            // Both are written from code rather than bound, so a language switch does not reach
            // them on its own. The greeting had been going stale here since it was added; the
            // installed-languages line under it would have done the same.
            RefreshSignedInUser();
            RefreshInstalledLanguagesText();
            RefreshAdvancedSearch();
        }
        finally
        {
            _suppressLanguageRefresh = false;
        }
    }

    private void OnEditBrandingClick(object sender, RoutedEventArgs e)
    {
        if (!AuthenticationService.Instance.CanConfigureShop)
            return;

        var window = new ReceiptBrandingWindow(_localization) { Owner = this };
        window.ShowDialog();
    }

    /// <summary>Opens the settlement report, and refreshes the summary strip once it closes.</summary>
    /// <remarks>
    /// Reachable from the menu AND by clicking the strip, because the strip is a summary of exactly
    /// what the report says — a reader who wants more of it should not have to find a menu.
    /// </remarks>
    private void OnSettlementClick(object sender, RoutedEventArgs e)
    {
        if (!AuthenticationService.Instance.CanViewReports)
            return;

        var window = new SettlementWindow(_scopeFactory, _localization) { Owner = this };
        window.ShowDialog();
        RefreshSummaryStrip();
    }

    private void OnSummaryClick(object sender, MouseButtonEventArgs e) => OnSettlementClick(sender, e);

    /// <summary>
    /// Fills the "this month" strip above the records list.
    /// </summary>
    /// <remarks>
    /// Through the SAME <see cref="SettlementCalculator"/> the report window uses. It would have been
    /// a handful of Sum() calls to total the loaded page here instead, and that is the version that
    /// goes wrong: the page is twenty orders, the strip would silently describe them rather than the
    /// month, and the two screens would disagree about the shop's takings.
    ///
    /// Hidden outright when the period earned nothing, rather than showing a row of zeroes that
    /// reads as a fault on a shop's first day.
    /// </remarks>
    private void RefreshSummaryStrip()
    {
        // THE ONE OWNER of this strip's visibility, and it has to be — the capability check used to
        // live in ApplyRolePermissions, which runs first and was then overwritten by the "the month
        // has figures, so show it" line below on the very next order reload. The strip reappeared
        // for a role that may not read reports, and nothing looked wrong in either method.
        if (!AuthenticationService.Instance.CanViewReports)
        {
            SummaryStrip.Visibility = Visibility.Collapsed;
            return;
        }

        var period = DateRange.CurrentMonth();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var orders = db.Orders.AsNoTracking().ToList();

        var report = SettlementCalculator.For(
            orders, period, ShopContext.Instance.Current?.CurrencyType ?? CurrencyType.CAD);

        if (report.IsEmpty)
        {
            SummaryStrip.Visibility = Visibility.Collapsed;
            return;
        }

        string Money(decimal amount)
            => CurrencySettingService.GetSymbol(report.Currency)
               + amount.ToString("N2", CultureInfo.InvariantCulture);

        SummaryStrip.Visibility = Visibility.Visible;
        SummaryTitleText.Text = _localization["Main.Summary.Title"];
        SummaryPeriodText.Text = period.Title(_localization, CultureInfo.CurrentUICulture);
        SummaryOpenText.Text = _localization["Main.Summary.OpenReport"];

        SummaryMetricList.ItemsSource = new[]
        {
            new SummaryMetric(_localization["Settlement.PostTax"], Money(report.PostTaxTotal), "#111827"),
            new SummaryMetric(_localization["Settlement.Received"], Money(report.ReceivedTotal), "#15803D"),
            new SummaryMetric(_localization["Settlement.Outstanding"], Money(report.OutstandingTotal), "#B45309"),
            new SummaryMetric(_localization["Settlement.Tax"], Money(report.TaxTotal), "#6B7280"),
            new SummaryMetric(
                _localization["Settlement.Orders.Unfinished"],
                report.Counts.Unfinished.ToString(CultureInfo.InvariantCulture),
                "#4F46E5")
        };
    }

    /// <summary>One figure on the summary strip. Bound from MainWindow.xaml.</summary>
    internal sealed record SummaryMetric(string Caption, string Value, string Accent);

    private void OnMeasurementTermsClick(object sender, RoutedEventArgs e)
    {
        if (!AuthenticationService.Instance.CanConfigureShop)
            return;

        var window = new MeasurementTermsWindow { Owner = this };
        window.ShowDialog();
    }

    private void OnProductCatalogClick(object sender, RoutedEventArgs e)
    {
        if (!AuthenticationService.Instance.CanConfigureShop)
            return;

        var window = new ProductCatalogWindow(_localization) { Owner = this };
        window.ShowDialog();

        // The open order editors build their category drop-downs when a row is created, so a
        // catalogue edited underneath them would leave stale lists on screen. Refreshing the list
        // is enough here — the editors are modal to their own windows and rebuild on next open.
        _ = _viewModel.LoadOrdersAsync();
    }

    // --- Shops (Local Configuration → Switch Shop / Shop Settings) ------------------------------

    private void OnSwitchShopClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureNoOpenOrderWindows("Shop.Switch.CloseEditors", "Toolbar.SwitchShop"))
            return;

        var picker = new ShopPickerWindow(
            _localization,
            _scopeFactory,
            AuthenticationService.Instance.CurrentUser,
            ShopContext.Instance.Current) { Owner = this };

        if (picker.ShowDialog() is not true || picker.SelectedShop is null)
            return;

        OpenShop(picker.SelectedShop);

        // Deferred to here for the same reason as on the startup path: the terms editor writes to
        // whichever shop is bound, so the new shop has to be open first.
        if (picker.ConfigureTermsRequested)
            new MeasurementTermsWindow { Owner = this }.ShowDialog();
    }

    private void OnStoreMembersClick(object sender, RoutedEventArgs e)
    {
        // Defence in depth: the button is hidden for staff, but the check belongs where the action
        // happens too.
        if (!AuthenticationService.Instance.CanManageStoreMembers
            || ShopContext.Instance.Current is not { } current)
        {
            return;
        }

        new StoreMembersWindow(_localization, current) { Owner = this }.ShowDialog();

        // A manager can deactivate their OWN membership from here — the service refuses it for the
        // open shop, but they can still change their roles. Re-gate rather than trust the chrome.
        ApplyRolePermissions();
    }

    /// <summary>
    /// Opens the permission panel, and re-gates this window afterwards.
    /// </summary>
    /// <remarks>
    /// The re-gate is not housekeeping. An administrator can edit the role they themselves hold in
    /// the open shop, so the menus around this window may describe permissions that stopped existing
    /// while the panel was on screen.
    /// </remarks>
    private void OnPermissionsClick(object sender, RoutedEventArgs e)
    {
        if (!AuthenticationService.Instance.CanManagePermissions)
            return;

        new PermissionsWindow(_localization, _scopeFactory) { Owner = this }.ShowDialog();

        ApplyRolePermissions();
    }

    private async void OnUserManagementClick(object sender, RoutedEventArgs e)
    {
        // Defence in depth: the menu item is hidden for non-administrators, but the check belongs
        // where the action happens too.
        if (!AuthenticationService.Instance.CanManageUsers)
            return;

        var users = new UserManagementWindow(_localization, _scopeFactory) { Owner = this };
        users.ShowDialog();

        // "Sign in as this user" ends THIS session, so it takes the same route sign-out does: the
        // application tears the main window down and re-runs the shop picker as the new person.
        // Nothing below runs — this window is one of the things being closed.
        if (users.SignInAsUserName is { } userName)
        {
            await SwitchUserAsync(userName);
            return;
        }

        // An administrator can revoke their own access to the open shop here. Their capabilities in
        // it are resolved from the assignments that were just rewritten, so the chrome has to be
        // re-gated even though the shop itself did not change.
        ApplyRolePermissions();
    }

    /// <summary>
    /// Hands the session to another account. Wrapped for the same reason as sign-out: the caller is
    /// an <c>async void</c> handler on a window that is about to close, so an exception here would
    /// take the dispatcher down with no explanation.
    /// </summary>
    private async Task SwitchUserAsync(string userName)
    {
        try
        {
            await ((App)Application.Current).SignInAsAsync(userName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"{_localization["SignOut.Failed"]}{Environment.NewLine}{Environment.NewLine}{ex}",
                _localization["App.MainTitle"],
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnShopSettingsClick(object sender, RoutedEventArgs e)
    {
        // Defence in depth: the menu item is hidden for staff, but the check belongs where the
        // action happens too.
        if (!AuthenticationService.Instance.CanConfigureShop || ShopContext.Instance.Current is not { } current)
            return;

        var setup = new ShopSetupWindow(_localization, _scopeFactory, current) { Owner = this };
        if (setup.ShowDialog() is not true || setup.Shop is null)
            return;

        // Re-opened rather than mutated in place: the saved instance came from a different
        // DbContext, and rebinding is what refreshes the header name, the currency symbol and the
        // measurement-terms file in one step.
        OpenShop(setup.Shop);
    }

    private void OpenShop(Shop shop)
    {
        ((App)Application.Current).OpenShop(shop);

        // The order list is filtered by shop, and the currency symbol is rendered per row, so the
        // list has to be rebuilt even when the shop only had its settings edited.
        _ = _viewModel.LoadOrdersAsync();
    }

    /// <summary>
    /// Blocks a shop switch or sign-out while an order editor is open. The editor holds an order
    /// belonging to the shop being left; once the active shop changes, AppDbContext filters that
    /// order out, so saving would fail to find its own row. Sign-out has the same problem plus an
    /// orphaned window outliving its main window. Cheaper to refuse than to explain afterwards.
    /// </summary>
    private bool EnsureNoOpenOrderWindows(string messageKey, string titleKey)
    {
        var openEditor = Application.Current.Windows.OfType<OrderEditWindow>().FirstOrDefault();
        if (openEditor is null)
            return true;

        MessageBox.Show(
            this,
            _localization[messageKey],
            _localization[titleKey],
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        openEditor.Activate();
        return false;
    }
}
