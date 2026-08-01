using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System.Diagnostics;
using System.Globalization;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using CameywareOrder.Converters;
using CameywareOrder.Data;
using CameywareOrder.Localization;
using CameywareOrder.Models;
using CameywareOrder.Services;
using CameywareOrder.ViewModels;
using CameywareOrder.Views;
using System.Diagnostics.CodeAnalysis;

namespace CameywareOrder;

public partial class MainWindow : Window
{
    private static readonly string ExplorerPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    // Shared file-dialog filter for the JSON import/export dialogs (measurement terms, branding).
    private const string JsonFileFilter = "JSON (*.json)|*.json";

    private readonly MainViewModel _viewModel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LocalizationService _localization;
    private bool _isLanguageSwitchInitializing;
    private bool _suppressLanguageRefresh;

    public MainWindow(
        MainViewModel viewModel,
        IServiceScopeFactory scopeFactory,
        LocalizationService localization)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _scopeFactory = scopeFactory;
        _localization = localization;
        DataContext = _viewModel;

        ApplyRolePermissions();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.SelectionRequested += OnSelectionRequested;
        _localization.LanguageChanged += OnLanguageChangedGlobally;
        ShopContext.Instance.ShopChanged += OnShopChanged;
        RefreshToolbarLabels();
        _ = _viewModel.LoadOrdersAsync();
    }

    // Shipped is treated as a finalized/completed state (the order has already been
    // delivered to the customer), so it is read-only just like Completed/Cancelled/Returned.
    private static bool IsReadOnlyStatus(OrderStatus status)
        => status is OrderStatus.Shipped or OrderStatus.Completed or OrderStatus.Cancelled or OrderStatus.Returned;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedOrder))
            RefreshToolbarLabels();
    }

    private void RefreshToolbarLabels()
    {
        var selectedStatus = _viewModel.SelectedOrder?.Status;
        var isReadOnly = selectedStatus.HasValue && IsReadOnlyStatus(selectedStatus.Value);
        var label = _localization[isReadOnly ? "Toolbar.ViewOrder" : "Toolbar.EditOrder"];
        EditOrderButton.Content = label;
        EditContextMenuItem.Header = label;
    }

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

        // A manager configures the shop they run; staff take orders in it.
        var configure = Show(auth.CanConfigureShop);
        ShopSettingsMenuItem.Visibility = configure;
        MeasurementTermsMenuItem.Visibility = configure;
        ProductCatalogMenuItem.Visibility = configure;
        HeaderFooterMenuItem.Visibility = configure;

        // Whole-installation tools, and the database path they act on.
        var dataTools = Show(auth.CanUseDataTools);
        LocalDatabaseMenuItem.Visibility = dataTools;
        ImportExportMenuItem.Visibility = dataTools;
        DataPathSeparator.Visibility = dataTools;
        DataPathLabelItem.Visibility = dataTools;
        DataPathValueItem.Visibility = dataTools;

        UserManagementMenuItem.Visibility = Show(auth.CanManageUsers);
        StoreMembersButton.Visibility = Show(auth.CanManageStoreMembers);

        // Hidden when everything below it is: a separator with nothing under it reads as a menu
        // that failed to load.
        ConfigSeparator.Visibility = Show(auth.CanConfigureShop || auth.CanUseDataTools);

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
        var role = UserPresentation.RoleText(_localization, auth.CurrentRole);
        GreetingText.Text = _localization.Format("Main.Greeting", user.GreetingName, role);
        GreetingText.ToolTip = _localization.Format("Shop.Picker.SignedInAs", user.UserName, role);
    }

    /// <summary>
    /// Drops the subscriptions this window and its view model hold on the application singletons.
    /// Signing out builds a NEW MainWindow, so without this each sign-out would leave a dead
    /// window listening for language and shop changes and updating controls nobody can see.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        _localization.LanguageChanged -= OnLanguageChangedGlobally;
        ShopContext.Instance.ShopChanged -= OnShopChanged;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.SelectionRequested -= OnSelectionRequested;
        _viewModel.Detach();

        base.OnClosed(e);
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
        }
        finally
        {
            _suppressLanguageRefresh = false;
        }
    }

    // Defence in depth on every handler below the Local Database and Import/Export menus: those menus are
    // hidden for non-administrators, but a hidden menu is a fact about the UI, not a permission.
    private void OnOpenDataFolderClick(object sender, RoutedEventArgs e)
    {
        if (!AuthenticationService.Instance.CanUseDataTools)
            return;

        try
        {
            var dbPath = DatabasePathProvider.DatabaseFilePath;
            var folderPath = System.IO.Path.GetDirectoryName(dbPath);
            if (string.IsNullOrWhiteSpace(folderPath))
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = ExplorerPath,
                Arguments = $"\"{folderPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = _localization.Format("Status.OpenFolderFailed", ex.Message);
        }
    }

    private void OnCopyDataPathClick(object sender, RoutedEventArgs e)
    {
        if (!AuthenticationService.Instance.CanUseDataTools)
            return;

        try
        {
            Clipboard.SetText(DatabasePathProvider.DatabaseFilePath);
            _viewModel.StatusMessage = _localization["Status.CopyPathSucceeded"];
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = _localization.Format("Status.CopyPathFailed", ex.Message);
        }
    }

    private void OnRevealDataFileClick(object sender, RoutedEventArgs e)
    {
        if (!AuthenticationService.Instance.CanUseDataTools)
            return;

        try
        {
            var dbPath = DatabasePathProvider.DatabaseFilePath;
            if (!System.IO.File.Exists(dbPath))
            {
                OnOpenDataFolderClick(sender, e);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = ExplorerPath,
                Arguments = $"/select,\"{dbPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = _localization.Format("Status.RevealFileFailed", ex.Message);
        }
    }

    /// <summary>
    /// Hands the list's selection to the view model, which is what Copy and Delete act on.
    /// </summary>
    /// <remarks>
    /// A ListView's <c>SelectedItems</c> is not a dependency property, so it cannot be bound and has
    /// to be pushed. One direction only: the view model never writes back from here, so reporting a
    /// selection cannot re-enter through the event that reported it.
    /// </remarks>
    private void OnOrdersSelectionChanged(object sender, SelectionChangedEventArgs e)
        => _viewModel.SetSelection(OrdersListView.SelectedItems.OfType<Order>());

    /// <summary>
    /// Selects exactly the rows the view model asks for — used after a batch copy, so the copies
    /// end up selected the way a single copy has always left its copy selected.
    /// </summary>
    private void OnSelectionRequested(object? sender, IReadOnlyList<Order> orders)
    {
        OrdersListView.SelectedItems.Clear();
        foreach (var order in orders)
            OrdersListView.SelectedItems.Add(order);

        if (orders.Count > 0)
            OrdersListView.ScrollIntoView(orders[0]);
    }

    private void OnOrderRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Exactly one row: a double-click that lands on a batch would open whichever order happened
        // to be the anchor, silently ignoring the rest of what is selected.
        if (!_viewModel.HasSingleSelection)
            return;

        OnEditOrderClick(sender, new RoutedEventArgs());
    }

    /// <summary>
    /// Left and Right arrow page the order list, from anywhere in the window.
    /// </summary>
    /// <remarks>
    /// Accessibility: paging was reachable only by clicking two small buttons at the bottom of the
    /// list. Arrow keys give the whole list keyboard-only navigation without a modifier chord to
    /// memorise, and the page summary is a polite live region so a screen reader says where you
    /// landed.
    ///
    /// PreviewKeyDown on the window rather than a <c>KeyBinding</c>: an InputBinding fires no matter
    /// what has focus, which would page the list every time someone moved the caret in the search
    /// box. Handling it here lets <see cref="ConsumesHorizontalArrows"/> stand down for controls
    /// that own the key.
    ///
    /// Any modifier stands down too — Alt+Left is "back" almost everywhere, and Ctrl+Left is
    /// word-wise caret movement. Neither should be quietly redefined as "previous page".
    /// </remarks>
    private void OnMainWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        // ESC is "I am leaving this machine". It offers the choice rather than acting, because the
        // two things it can mean — lock, or sign out — are not interchangeable and the key is easy
        // to hit by accident.
        //
        // Modifier-free only, and only from the window itself: a combo box or a context menu that is
        // open owns ESC to close itself, and it is already handled by the time this tunnels past.
        if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None && !e.Handled)
        {
            e.Handled = true;
            OfferSessionChoice();
            return;
        }

        if (e.Key is not (Key.Left or Key.Right))
            return;

        if (Keyboard.Modifiers != ModifierKeys.None)
            return;

        if (ConsumesHorizontalArrows(Keyboard.FocusedElement as DependencyObject))
            return;

        var command = e.Key == Key.Right ? _viewModel.NextPageCommand : _viewModel.PreviousPageCommand;
        if (!command.CanExecute(null))
            return;

        e.Handled = true;
        command.Execute(null);

        AnnouncePageChange();
        FocusFirstOrder();
    }

    /// <summary>
    /// Whether the focused element, or anything it sits inside, needs the horizontal arrows itself.
    /// </summary>
    /// <remarks>
    /// Walks up the tree because focus usually lands on a part inside the control — the editable
    /// TextBox of a ComboBox, or a DatePicker's inner text box — and testing only the focused
    /// element would miss it and steal the key anyway.
    /// </remarks>
    private static bool ConsumesHorizontalArrows(DependencyObject? focused)
    {
        for (var node = focused; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is TextBoxBase or PasswordBox or ComboBox or DatePicker or Slider or MenuBase)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Nudges the live region so a screen reader re-reads it. Rebinding the text alone does not
    /// raise the event, so the announcement has to be asked for explicitly.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static",
        Justification = "False positive: PageSummaryText is an x:Name instance field from the XAML-generated " +
                        "partial, which the analyzer does not see. The method reads instance data and cannot be static.")]
    private void AnnouncePageChange()
    {
        var peer = UIElementAutomationPeer.FromElement(PageSummaryText)
            ?? UIElementAutomationPeer.CreatePeerForElement(PageSummaryText);

        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    /// <summary>
    /// Puts selection and focus on the first row of the freshly loaded page.
    /// </summary>
    /// <remarks>
    /// Without this the keyboard user is left on a page whose rows they cannot reach with Up/Down
    /// until they Tab back into the list, and a screen reader has nothing to read. The container is
    /// generated asynchronously, so this waits for the item containers rather than assuming them.
    /// </remarks>
    private void FocusFirstOrder()
    {
        if (OrdersListView.Items.Count == 0)
            return;

        OrdersListView.SelectedIndex = 0;
        OrdersListView.ScrollIntoView(OrdersListView.Items[0]);

        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                if (OrdersListView.ItemContainerGenerator.ContainerFromIndex(0) is ListViewItem row)
                    row.Focus();
            }),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // Enter opens the selected order, as a double-click does. Delete removes the whole selection.
    //
    // The two keys are gated differently on purpose: Enter opens ONE order and so needs exactly one
    // selected, while Delete is a batch action and the command it routes to owns both the "one" and
    // the "several" wording. Ctrl+A is not handled here at all — ListBox has its own SelectAll
    // command binding on that gesture, live in Extended mode, and it reaches the loaded items,
    // which is the current page.
    private void OnOrderRowKeyDown(object sender, KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        switch (e.Key)
        {
            case Key.Enter:
                if (!_viewModel.HasSingleSelection)
                    return;
                e.Handled = true;
                OnEditOrderClick(sender, new RoutedEventArgs());
                break;
            case Key.Delete:
                e.Handled = true;
                if (_viewModel.DeleteOrderCommand.CanExecute(null))
                    _viewModel.DeleteOrderCommand.Execute(null);
                break;
        }
    }

    /// <summary>
    /// Makes a right-click land on the row the pointer is over. WPF does not select on right-click,
    /// so without this the context menu would act on whatever was selected before.
    /// </summary>
    /// <remarks>
    /// Right-clicking INSIDE an existing selection leaves it alone — that is how the menu comes to
    /// act on a batch. Right-clicking outside it means "this one instead", so the selection is
    /// REPLACED rather than extended: setting <c>IsSelected</c> on its own adds the row in Extended
    /// mode, and a Delete that quietly takes one more record than the user pointed at is the worst
    /// version of this control.
    /// </remarks>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static",
        Justification = "Named from XAML (EventSetter Handler=\"OnOrderRowRightClick\"). The generated " +
                        "InitializeComponent wires it as this.OnOrderRowRightClick, which does not compile " +
                        "against a static method.")]
    private void OnOrderRowRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListViewItem item || item.IsSelected)
            return;

        OrdersListView.SelectedItems.Clear();
        item.IsSelected = true;
    }

    // Keeps the trailing (Notes) column filling the remaining width as the list resizes.
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static",
        Justification = "Named from XAML (SizeChanged=\"OnOrdersListSizeChanged\"); see OnOrderRowRightClick.")]
    private void OnOrdersListSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not ListView { View: GridView grid } list || grid.Columns.Count == 0)
            return;

        double used = 0;
        for (int i = 0; i < grid.Columns.Count - 1; i++)
            used += grid.Columns[i].ActualWidth;

        double remaining = list.ActualWidth - used - 28; // account for border + scrollbar
        if (remaining > 200)
            grid.Columns[^1].Width = remaining;
    }

    // Sort the orders list when a sortable column header is clicked. Each click toggles
    // ascending/descending on that column; clicking a new column starts ascending.
    private void OnOrderColumnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader header
            || header.Role == GridViewColumnHeaderRole.Padding
            || header.Column is null)
            return;

        var sortKey = OrderColumnSort.GetSortKey(header.Column);
        if (string.IsNullOrEmpty(sortKey))
            return;

        _viewModel.SortBy(sortKey);
        UpdateSortGlyphs();
    }

    // Reflect the active sort on the column headers with an up/down arrow glyph.
    private void UpdateSortGlyphs()
    {
        if (OrdersListView.View is not GridView grid)
            return;

        var arrow = _viewModel.SortAscending ? " \u25B2" : " \u25BC";
        foreach (var column in grid.Columns)
        {
            var key = OrderColumnSort.GetSortKey(column);
            OrderColumnSort.SetSortGlyph(column,
                !string.IsNullOrEmpty(key) && key == _viewModel.SortKey ? arrow : string.Empty);
        }
    }

    private void OnContextEditClick(object sender, RoutedEventArgs e)
        => OnEditOrderClick(sender, e);

    private void OnContextCopyClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.CopyOrderCommand.CanExecute(null))
            _viewModel.CopyOrderCommand.Execute(null);
    }

    private void OnContextDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.DeleteOrderCommand.CanExecute(null))
            _viewModel.DeleteOrderCommand.Execute(null);
    }

    private void OnContextPrintClick(object sender, RoutedEventArgs e)
        => OnPrintReceiptClick(sender, e);

    private void OnContextPrintMeasurementsClick(object sender, RoutedEventArgs e)
        => OnPrintMeasurementsClick(sender, e);

    private void OnContextPrintReceiptAndMeasurementsClick(object sender, RoutedEventArgs e)
        => OnPrintReceiptAndMeasurementsClick(sender, e);

    private void OnAddOrderClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OrderEditWindow(_scopeFactory, _localization) { Owner = this };
        if (dialog.ShowDialog() == true)
            _ = _viewModel.LoadOrdersAsync();
    }

    private void OnEditOrderClick(object sender, RoutedEventArgs e)
    {
        // The shared entry point for the button, the context menu, Enter and the double-click, so
        // the "exactly one row" rule is restated here rather than trusted to four callers.
        if (!_viewModel.HasSingleSelection || _viewModel.SelectedOrder is null) return;
        var dialog = new OrderEditWindow(_scopeFactory, _localization, _viewModel.SelectedOrder) { Owner = this };
        if (dialog.ShowDialog() == true)
            _ = _viewModel.LoadOrdersAsync();
    }

    private void OnPrintReceiptClick(object sender, RoutedEventArgs e)
    {
        var order = _viewModel.SelectedOrder;
        if (order is null)
            return;

        try
        {
            var printDialog = new PrintDialog();
            if (printDialog.ShowDialog() != true)
                return;

            var document = BuildReceiptDocument(order, printDialog.PrintableAreaWidth);
            printDialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, _localization["Receipt.Title"]);
            _viewModel.StatusMessage = _localization["Status.PrintSucceeded"];
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = _localization.Format("Status.PrintFailed", ex.Message);
        }
    }

    private void OnPrintMeasurementsClick(object sender, RoutedEventArgs e)
        => PrintMeasurements(includeReceipt: false);

    private void OnPrintReceiptAndMeasurementsClick(object sender, RoutedEventArgs e)
        => PrintMeasurements(includeReceipt: true);

    // Shared entry point for the two measurement print actions. Asks the user for the
    // measurement language and unit, then prints either a measurements-only document or a
    // receipt followed (on a new page) by all garment measurements.
    private void PrintMeasurements(bool includeReceipt)
    {
        var order = _viewModel.SelectedOrder;
        if (order is null || !order.HasCustomMadeService)
            return;

        var optionsWindow = new MeasurementPrintOptionsWindow(_localization) { Owner = this };
        if (optionsWindow.ShowDialog() != true)
            return;

        var languageCode = optionsWindow.SelectedLanguageCode;
        var isInch = optionsWindow.IsInch;

        try
        {
            var printDialog = new PrintDialog();
            if (printDialog.ShowDialog() != true)
                return;

            FlowDocument document;
            if (includeReceipt)
            {
                document = BuildReceiptDocument(order, printDialog.PrintableAreaWidth);
                AddMeasurementSections(document, order, languageCode, isInch, pageBreakBefore: true);
            }
            else
            {
                document = BuildMeasurementDocument(order, languageCode, isInch, printDialog.PrintableAreaWidth);
            }

            printDialog.PrintDocument(
                ((IDocumentPaginatorSource)document).DocumentPaginator,
                _localization["Customer.Measurements.PrintTitle"]);
            _viewModel.StatusMessage = _localization["Status.PrintSucceeded"];
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = _localization.Format("Status.PrintFailed", ex.Message);
        }
    }

    // A standalone measurements-only document: shop branding, the order's key info, then
    // every garment's measurements in the chosen language and unit.
    private FlowDocument BuildMeasurementDocument(Order order, string languageCode, bool isInch, double pageWidth)
    {
        var document = new FlowDocument
        {
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = 12,
            // Wider side margins than top/bottom: printed output is read as a narrow column, and
            // the extra gutter is what keeps the panels off the paper edge.
            PagePadding = new Thickness(48, 40, 48, 40),
            PageWidth = pageWidth,
            ColumnWidth = pageWidth
        };

        var brandingSettings = ReceiptBrandingStore.Load();
        var branding = brandingSettings.ForLanguage(_localization.CurrentLanguageCode);
        var hasHeader = !BrandingRenderer.IsEmpty(branding.HeaderXaml);

        // The same generated letterhead the receipt prints, and for the same reason: without it the
        // sheet opened with a bare Receipt.TaxNumberLine above its own title and never named the shop.
        // The document title lives IN the letterhead as its subtitle, so the sections must not add
        // one of their own — hence includeTitle: hasHeader is false here.
        AddMeasurementLetterhead(document, languageCode, hasHeader);
        AddMeasurementSections(document, order, languageCode, isInch, pageBreakBefore: false, includeTitle: hasHeader);

        // insertTaxNumber: false — the letterhead above has already placed it, last, where it reads
        // as part of the letterhead instead of standing in for one.
        InjectReceiptBranding(document, brandingSettings, branding, insertTaxNumber: false);

        return document;
    }

    /// <summary>
    /// The generated letterhead on a standalone measurements sheet: shop name, document title,
    /// contact lines, GST/HST. Skipped when a custom header replaces it, exactly as on the receipt.
    /// </summary>
    private void AddMeasurementLetterhead(FlowDocument document, string languageCode, bool hasHeader)
    {
        if (hasHeader)
            return;

        var letterhead = ShopLetterhead.Build(_localization, languageCode, "Customer.Measurements.PrintTitle");

        document.Blocks.Add(new Paragraph(new Bold(new Run(letterhead.Name)))
        {
            FontSize = 18,
            TextAlignment = TextAlignment.Left,
            Margin = new Thickness(0, 0, 0, 2)
        });
        document.Blocks.Add(new Paragraph(new Run(letterhead.Subtitle ?? string.Empty))
        {
            TextAlignment = TextAlignment.Left,
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(0, 0, 0, 6)
        });

        AddLetterheadContactLines(document, letterhead);

        if (letterhead.TaxLine is not null)
        {
            document.Blocks.Add(new Paragraph(new Run(letterhead.TaxLine))
            {
                FontSize = 11,
                TextAlignment = TextAlignment.Left,
                Foreground = System.Windows.Media.Brushes.DimGray,
                Margin = new Thickness(0, 0, 0, 10)
            });
        }
    }

    // Renders the measurement content into an existing document. When pageBreakBefore is
    // true (receipt + measurements) the first block starts on a fresh page.
    private void AddMeasurementSections(
        FlowDocument document, Order order, string languageCode, bool isInch, bool pageBreakBefore, bool includeTitle = true)
    {
        // Suppressed on a standalone sheet whose generated letterhead already carries the title as
        // its subtitle; kept for the receipt+measurements document, where this heading opens a new
        // page far below the letterhead and is the only thing naming what follows.
        if (includeTitle)
        {
            var title = ReceiptSectionTitle(_localization.GetText("Customer.Measurements.PrintTitle", languageCode));
            if (pageBreakBefore)
                title.BreakPageBefore = true;
            document.Blocks.Add(title);
        }

        document.Blocks.Add(ReceiptInfoLine(
            _localization.GetText("Order.Fields.OrderNumber", languageCode), order.OrderNumber));
        AddReceiptInfoLineIfHasValue(document.Blocks,
            _localization.GetText("Order.Fields.CustomerName", languageCode), order.CustomerName);

        var unitLabel = _localization.GetText("Measure.Unit.Label", languageCode);
        var unitValue = _localization.GetText(isInch ? "Measure.Unit.Inch" : "Measure.Unit.Cm", languageCode);
        document.Blocks.Add(ReceiptInfoLine(unitLabel, unitValue));

        document.Blocks.Add(ReceiptServiceDivider());

        foreach (var record in order.CustomMadeRecords)
        {
            foreach (var (garmentTitle, rows) in CustomMadeMeasurementReader.BuildSections(record, languageCode, isInch))
            {
                document.Blocks.Add(new Paragraph(new Bold(new Run(garmentTitle)))
                {
                    FontSize = 13,
                    Margin = new Thickness(0, 6, 0, 4)
                });

                foreach (var (label, value) in rows)
                    document.Blocks.Add(ReceiptInfoLine(label, value));

                document.Blocks.Add(ReceiptServiceDivider());
            }
        }
    }

    private void OnEditBrandingClick(object sender, RoutedEventArgs e)
    {
        if (!AuthenticationService.Instance.CanConfigureShop)
            return;

        var window = new ReceiptBrandingWindow(_localization) { Owner = this };
        window.ShowDialog();
    }

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

    // --- Import / export (Local Configuration → Import/Export) ----------------------------------

    // Appends today's date (yyyyMMdd) before the extension so exported files sort/archive
    // cleanly by date, e.g. "measurement-terms-20260726.json".
    private static string BuildDatedExportFileName(string baseName, string extension) =>
        $"{baseName}-{DateTime.Now:yyyyMMdd}.{extension}";

    private void OnExportMeasurementTermsClick(object sender, RoutedEventArgs e)
    {
        if (!AuthenticationService.Instance.CanUseDataTools)
            return;

        var dialog = new SaveFileDialog
        {
            FileName = BuildDatedExportFileName("measurement-terms", "json"),
            Filter = JsonFileFilter
        };

        if (dialog.ShowDialog(this) is not true)
            return;

        try
        {
            System.IO.File.WriteAllText(dialog.FileName, MeasurementTermsService.Instance.ExportConfigJson());
            _viewModel.StatusMessage = _localization["Status.ExportMeasurementTermsSucceeded"];
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = _localization.Format("Status.ExportMeasurementTermsFailed", ex.Message);
        }
    }

    private void OnImportMeasurementTermsClick(object sender, RoutedEventArgs e)
    {
        if (!AuthenticationService.Instance.CanUseDataTools)
            return;

        var dialog = new OpenFileDialog
        {
            Filter = JsonFileFilter,
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) is not true)
            return;

        MeasurementTermsConfig? imported;
        try
        {
            imported = MeasurementTermsService.TryParseConfigJson(System.IO.File.ReadAllText(dialog.FileName));
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = _localization.Format("Status.ImportMeasurementTermsFailed", ex.Message);
            return;
        }

        if (imported is null)
        {
            MessageBox.Show(
                _localization["Status.ImportMeasurementTermsInvalid"],
                _localization["MeasureTerms.Title"],
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            _localization["ImportExport.MeasurementTermsConfirm"],
            _localization["MeasureTerms.Title"],
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            MeasurementTermsService.Instance.ImportConfig(imported);
            _viewModel.StatusMessage = _localization["Status.ImportMeasurementTermsSucceeded"];
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = _localization.Format("Status.ImportMeasurementTermsFailed", ex.Message);
        }
    }

    private void OnExportDatabaseClick(object sender, RoutedEventArgs e)
    {
        if (!AuthenticationService.Instance.CanUseDataTools)
            return;

        // The exported package is a zip containing orders.db plus every attached
        // custom-made document image, so the export is self-contained and can be
        // copied to another PC without leaving image references dangling.
        var dialog = new SaveFileDialog
        {
            FileName = BuildDatedExportFileName("orders-backup", "zip"),
            Filter = "Backup Package (*.zip)|*.zip"
        };

        if (dialog.ShowDialog(this) is not true)
            return;

        try
        {
            DatabasePathProvider.ExportDatabaseTo(dialog.FileName);
            _viewModel.StatusMessage = _localization["Status.ExportDatabaseSucceeded"];
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = _localization.Format("Status.ExportDatabaseFailed", ex.Message);
        }
    }

    private void OnImportDatabaseClick(object sender, RoutedEventArgs e)
    {
        if (!AuthenticationService.Instance.CanUseDataTools)
            return;

        // Accepts the zip package produced by export (db + document images) as well as a
        // legacy raw .db file exported before document packaging existed.
        var dialog = new OpenFileDialog
        {
            Filter = "Backup Package (*.zip)|*.zip|SQLite Database (*.db)|*.db|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) is not true)
            return;

        // Destructive: replaces every order currently in the app. Requires explicit
        // confirmation; the current database is still auto-backed-up as an extra safety
        // net (see DatabasePathProvider.ImportDatabaseFrom).
        var confirm = MessageBox.Show(
            _localization["ImportExport.DatabaseConfirm"],
            _localization["Toolbar.LocalDatabase"],
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            DatabasePathProvider.ImportDatabaseFrom(dialog.FileName);
            _viewModel.LoadOrdersCommand.Execute(null);
            _viewModel.StatusMessage = _localization["Status.ImportDatabaseSucceeded"];
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = _localization.Format("Status.ImportDatabaseFailed", ex.Message);
        }
    }

    private void OnExportBrandingClick(object sender, RoutedEventArgs e)
    {
        if (!AuthenticationService.Instance.CanUseDataTools)
            return;

        var dialog = new SaveFileDialog
        {
            FileName = BuildDatedExportFileName("header-footer-branding", "json"),
            Filter = JsonFileFilter
        };

        if (dialog.ShowDialog(this) is not true)
            return;

        try
        {
            System.IO.File.WriteAllText(dialog.FileName, ReceiptBrandingStore.ExportConfigJson());
            _viewModel.StatusMessage = _localization["Status.ExportBrandingSucceeded"];
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = _localization.Format("Status.ExportBrandingFailed", ex.Message);
        }
    }

    private void OnImportBrandingClick(object sender, RoutedEventArgs e)
    {
        if (!AuthenticationService.Instance.CanUseDataTools)
            return;

        var dialog = new OpenFileDialog
        {
            Filter = JsonFileFilter,
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) is not true)
            return;

        BrandingExport? imported;
        try
        {
            imported = ReceiptBrandingStore.TryParseConfigJson(System.IO.File.ReadAllText(dialog.FileName));
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = _localization.Format("Status.ImportBrandingFailed", ex.Message);
            return;
        }

        if (imported is null)
        {
            MessageBox.Show(
                _localization["Status.ImportBrandingInvalid"],
                _localization["Toolbar.HeaderFooter"],
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            _localization["ImportExport.BrandingConfirm"],
            _localization["Toolbar.HeaderFooter"],
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            ReceiptBrandingStore.ImportConfig(imported);
            _viewModel.StatusMessage = _localization["Status.ImportBrandingSucceeded"];
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = _localization.Format("Status.ImportBrandingFailed", ex.Message);
        }
    }

    // One-click backup of everything this machine holds: the order database with its attached
    // images, measurement terms, receipt branding (logo included), currency and language.
    private void OnExportGlobalSettingsClick(object sender, RoutedEventArgs e)
    {
        if (!AuthenticationService.Instance.CanUseDataTools)
            return;

        var dialog = new SaveFileDialog
        {
            FileName = BuildDatedExportFileName("leeyonge-global-settings", "zip"),
            Filter = "Backup Package (*.zip)|*.zip"
        };

        if (dialog.ShowDialog(this) is not true)
            return;

        try
        {
            GlobalSettingsPackage.ExportTo(dialog.FileName);
            _viewModel.StatusMessage = _localization["Status.ExportGlobalSettingsSucceeded"];
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = _localization.Format("Status.ExportGlobalSettingsFailed", ex.Message);
        }
    }

    private void OnImportGlobalSettingsClick(object sender, RoutedEventArgs e)
    {
        if (!AuthenticationService.Instance.CanUseDataTools)
            return;

        var dialog = new OpenFileDialog
        {
            Filter = "Backup Package (*.zip)|*.zip|All Files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) is not true)
            return;

        // Read and validate before touching anything, so an unreadable file changes nothing.
        var payload = GlobalSettingsPackage.TryRead(dialog.FileName);
        if (payload is null)
        {
            MessageBox.Show(
                _localization["Status.ImportGlobalSettingsInvalid"],
                _localization["Toolbar.GlobalSettings"],
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // This is the most destructive import in the app — it replaces the order data as well
        // as every local setting — so the confirmation spells out what the package will apply.
        var confirm = MessageBox.Show(
            _localization.Format("ImportExport.GlobalSettingsConfirm", DescribePackageContents(payload)),
            _localization["Toolbar.GlobalSettings"],
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            GlobalSettingsPackage.Import(dialog.FileName, payload);
            _viewModel.LoadOrdersCommand.Execute(null);
            _viewModel.StatusMessage = _localization["Status.ImportGlobalSettingsSucceeded"];
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = _localization.Format("Status.ImportGlobalSettingsFailed", ex.Message);
        }
    }

    // Lists only the parts the package actually carries, so the confirmation never promises to
    // restore something the file does not contain.
    private string DescribePackageContents(GlobalSettingsExport payload)
    {
        var parts = new List<string>();
        if (payload.ContainsDatabase)
            parts.Add(_localization["Toolbar.LocalDatabase"]);
        if (payload.MeasurementTerms is not null)
            parts.Add(_localization["Toolbar.MeasurementTerms"]);
        if (payload.Branding is not null)
            parts.Add(_localization["Toolbar.HeaderFooter"]);
        if (payload.Currency is not null)
            parts.Add(_localization["Toolbar.CurrencySetting"]);
        if (!string.IsNullOrWhiteSpace(payload.LanguageCode))
            parts.Add(_localization["Toolbar.Language"]);

        return _localization.JoinList(parts);
    }

    private FlowDocument BuildReceiptDocument(Order order, double pageWidth)
    {
        // The ORDER's currency, never the shop's. A receipt is a record of what was charged, and a
        // shop that has since started taking a second currency must not reprint an old one in it.
        var currency = order.CurrencyType;

        var document = new FlowDocument
        {
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = 12,
            // Wider side margins than top/bottom: printed output is read as a narrow column, and
            // the extra gutter is what keeps the panels off the paper edge.
            PagePadding = new Thickness(48, 40, 48, 40),
            PageWidth = pageWidth,
            ColumnWidth = pageWidth
        };

        var brandingSettings = ReceiptBrandingStore.Load();
        var branding = brandingSettings.ForLanguage(_localization.CurrentLanguageCode);
        var hasHeader = !BrandingRenderer.IsEmpty(branding.HeaderXaml);

        AddReceiptTitle(document, hasHeader);
        AddReceiptCustomerInfo(document, order);

        document.Blocks.Add(ReceiptDivider());

        AddAlterationReceiptSection(document, order, currency);
        AddClothingReceiptSection(document, order, currency);
        AddCustomMadeReceiptSection(document, order, currency);

        AddReceiptTotals(document, order, currency);

        InjectReceiptBranding(document, brandingSettings, branding, insertTaxNumber: hasHeader);

        return document;
    }

    /// <summary>
    /// The default letterhead — shop name, subtitle, and the shop's contact details — shown when the
    /// header editor has no content of its own.
    /// </summary>
    /// <remarks>
    /// LEFT aligned, like everything else the receipt generates. Centred text reads as a decorative
    /// title; a business letterhead is a block of facts and belongs on the same left margin as the
    /// order details beneath it, so the eye follows one edge down the page.
    /// </remarks>
    private void AddReceiptTitle(FlowDocument document, bool hasHeader)
    {
        if (hasHeader)
            return;

        // The receipt is headed with the SHOP's own name, not a fixed app title — each branch
        // prints under its own name. Falls back to Main.HeaderTitle when no shop is open.
        document.Blocks.Add(new Paragraph(new Bold(new Run(ShopContext.Instance.CurrentName)))
        {
            FontSize = 18,
            TextAlignment = TextAlignment.Left,
            Margin = new Thickness(0, 0, 0, 2)
        });
        document.Blocks.Add(new Paragraph(new Run(_localization["Receipt.Title"]))
        {
            TextAlignment = TextAlignment.Left,
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(0, 0, 0, 6)
        });

        AddShopContactLines(document);

        // Last line of the letterhead, under the contact details. Placed here rather than by
        // InjectReceiptBranding, which inserts at the very TOP — correct when a custom header
        // replaces this block, but above the shop's own name when it does not.
        var taxNumber = CreateTaxNumberBlock(ResolveTaxRegistrationNumber(ReceiptBrandingStore.Load()));
        if (taxNumber is not null)
            document.Blocks.Add(taxNumber);
    }

    /// <summary>
    /// The shop's address, phone, email and website, each printed only when it has been filled in.
    /// </summary>
    /// <remarks>
    /// Part of the letterhead rather than of the order panel below: these describe the SHOP, and a
    /// customer looking for "where do I call about this" should find them next to the shop's name.
    ///
    /// One LABELLED line each, through the same <see cref="ReceiptInfoLine"/> the order panel uses,
    /// so "Address: …" on the letterhead reads the same way as "Customer name: …" below it. They
    /// were previously an unlabelled address line with the other three run together by a bullet,
    /// which left the reader to work out which was the phone and which the website.
    ///
    /// The labels are the SHOP's own field names (<c>Shop.Setup.*</c>), not the order's
    /// (<c>Order.Fields.*</c>): these are the shop's address and telephone, and the two sets exist
    /// separately for precisely that reason.
    ///
    /// Address comes from <see cref="Shop.ResolveAddress"/>, so a receipt printed in French carries
    /// the French wording of the address where one was entered. The other three are single-valued —
    /// a phone number is the same string in any language.
    ///
    /// At 10.5pt against the 12pt body: smaller than the order details, because this is reference
    /// information rather than something to read line by line, but not so small it stops being
    /// legible on a printed slip.
    /// </remarks>
    private void AddShopContactLines(FlowDocument document)
        => AddLetterheadContactLines(
            document,
            ShopLetterhead.Build(_localization, _localization.CurrentLanguageCode, "Receipt.Title"));

    /// <summary>
    /// The shop's address, phone, email and website as printed lines. Shared by the receipt and the
    /// measurements sheet so the two letterheads cannot drift apart again.
    /// </summary>
    private static void AddLetterheadContactLines(FlowDocument document, ShopLetterhead letterhead)
    {
        var written = false;

        foreach (var line in letterhead.ContactLines)
        {
            var paragraph = ReceiptInfoLine(line.Label, line.Value);

            // Tighter than the order panel's 3px leading: this is a four-line address block, and at
            // the panel's spacing it would occupy as much height as the order details themselves.
            paragraph.FontSize = 10.5;
            paragraph.TextAlignment = TextAlignment.Left;
            paragraph.Margin = new Thickness(0, 0, 0, 1);
            paragraph.Foreground = System.Windows.Media.Brushes.DimGray;

            document.Blocks.Add(paragraph);
            written = true;
        }

        // The gap belongs after the LAST line that was actually written — a fixed trailing margin on
        // the block would leave a hole under a shop that has filled nothing in.
        if (written && document.Blocks.LastBlock is Paragraph last)
            last.Margin = new Thickness(0, 0, 0, 10);
    }

    // Who and when, grouped into one panel so the money below reads as a separate thing.
    private void AddReceiptCustomerInfo(FlowDocument document, Order order)
    {
        var card = ReceiptCard(ReceiptCardBrush);
        var blocks = card.Blocks;

        blocks.Add(ReceiptInfoLine(_localization["Order.Fields.OrderNumber"], order.OrderNumber, bold: true));
        AddReceiptInfoLineIfHasValue(blocks, _localization["Order.Fields.CustomerName"], order.CustomerName);
        AddReceiptInfoLineIfHasValue(blocks, _localization["Order.Fields.PhoneNumber"], order.PhoneNumber);
        AddReceiptInfoLineIfHasValue(blocks, _localization["Order.Fields.Email"], order.Email);
        AddReceiptInfoLineIfHasValue(blocks, _localization["Order.Fields.Address"], order.Address);
        // The day only. The order form records a date — an order can be backdated to the day it was
        // actually taken — so a time here would print 00:00 on exactly those orders and read as a
        // fault. The receipt agrees with the list column and the detail panel line for line.
        blocks.Add(ReceiptInfoLine(
            _localization["Order.Fields.OrderDate"],
            order.OrderDateLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        blocks.Add(ReceiptInfoLine(_localization["Order.Fields.Status"], _localization[$"Status.{order.Status}"]));
        blocks.Add(ReceiptInfoLine(
            _localization["Order.Fields.CurrencyType"],
            ShopCurrencies.Name(order.CurrencyType, _localization)));
        var servicesSummary = new OrderServicesSummaryConverter().Convert(order, typeof(string), null, CultureInfo.CurrentCulture) as string;
        AddReceiptInfoLineIfHasValue(blocks, _localization["Order.Fields.ServiceType"], servicesSummary);
        // Who served this order, for the record. Omitted rather than blank when unknown: every order
        // saved before the column existed has no name, and a label with nothing beside it reads as a
        // printing fault rather than as an absence.
        AddReceiptInfoLineIfHasValue(blocks, _localization["Order.Fields.LastModifiedBy"], order.LastModifiedBy);

        document.Blocks.Add(card);
    }

    // Alterations service detail. Only shown when the section carries a charge and a
    // deposit method has been selected; otherwise the service is considered not added.
    private void AddAlterationReceiptSection(FlowDocument document, Order order, CurrencyType currency)
    {
        if (!order.AlterationAddedToReceipt)
            return;

        document.Blocks.Add(ReceiptSectionTitle(_localization["OrderEdit.Panel.Alterations"]));
        if (!string.IsNullOrWhiteSpace(order.ServiceDetails))
            document.Blocks.Add(new Paragraph(new Run(LocalizeWithFallback("Alteration.Category", order.ServiceDetails))) { Margin = new Thickness(0, 0, 0, 4) });

        document.Blocks.Add(ReceiptInfoLine(_localization["Order.Fields.Subtotal"], Money(currency, order.AlterationSubtotal ?? 0m)));
        if (order.AlterationTax > 0m)
            document.Blocks.Add(ReceiptInfoLine(_localization["Order.Fields.TaxAmount"], Money(currency, order.AlterationTax)));
        document.Blocks.Add(ReceiptInfoLine(_localization["Receipt.SectionTotal"], Money(currency, order.AlterationTotal), bold: true));
        document.Blocks.Add(ReceiptServiceDivider());
    }

    // Ready-made clothing / accessories. Only shown when the section carries a charge and a
    // deposit method has been selected; otherwise the service is considered not added.
    private void AddClothingReceiptSection(FlowDocument document, Order order, CurrencyType currency)
    {
        if (order.Items.Count == 0 || !order.ClothingAddedToReceipt)
            return;

        document.Blocks.Add(ReceiptSectionTitle(_localization["OrderEdit.Panel.ReadyMade"]));
        foreach (var item in order.Items)
        {
            var line = new Paragraph { Margin = new Thickness(0, 0, 0, 2) };
            var name = ProductCatalogService.Instance.ResolveName(item.ProductName);
            line.Inlines.Add(new Run($"{name}  {Money(currency, item.EffectiveUnitPrice)} x{item.Quantity}"));
            line.Inlines.Add(new Run($"    {Money(currency, item.TotalPrice)}") { FontWeight = FontWeights.SemiBold });
            document.Blocks.Add(line);
        }
        document.Blocks.Add(ReceiptInfoLine(_localization["Order.Fields.Subtotal"], Money(currency, order.ClothingSubtotal ?? 0m)));
        if (order.ClothingTax > 0m)
            document.Blocks.Add(ReceiptInfoLine(_localization["Order.Fields.TaxAmount"], Money(currency, order.ClothingTax)));
        document.Blocks.Add(ReceiptInfoLine(_localization["Receipt.SectionTotal"], Money(currency, order.ClothingTotal), bold: true));
        document.Blocks.Add(ReceiptServiceDivider());
    }

    // Custom-made records. Only shown when the section carries a charge and a deposit
    // method has been selected; otherwise the service is considered not added.
    private void AddCustomMadeReceiptSection(FlowDocument document, Order order, CurrencyType currency)
    {
        var customMadeRecords = order.CustomMadeRecords;
        if (customMadeRecords.Count == 0 || !order.CustomMadeAddedToReceipt)
            return;

        var summaryConverter = new CustomMadeRecordSummaryConverter();
        document.Blocks.Add(ReceiptSectionTitle(_localization["Detail.CustomMadeRecords"]));
        foreach (var record in customMadeRecords)
        {
            var summary = summaryConverter.Convert(record, typeof(string), null, CultureInfo.CurrentCulture) as string ?? string.Empty;
            var line = new Paragraph { Margin = new Thickness(0, 0, 0, 2) };
            line.Inlines.Add(new Run(summary));
            line.Inlines.Add(new Run($"    {Money(currency, record.SumTotal)}") { FontWeight = FontWeights.SemiBold });
            document.Blocks.Add(line);
        }
        document.Blocks.Add(ReceiptInfoLine(_localization["Receipt.SectionTotal"], Money(currency, order.CustomMadeTotal), bold: true));
        document.Blocks.Add(ReceiptServiceDivider());
    }

    /// <summary>
    /// The money the customer actually cares about, in its own tinted panel with a heavier top
    /// rule — on a printed page this is the block people look at first, and it should not have to
    /// be found among the service lines above it.
    /// </summary>
    private void AddReceiptTotals(FlowDocument document, Order order, CurrencyType currency)
    {
        var card = ReceiptCard(ReceiptTotalsBrush, topBorder: 2);
        var blocks = card.Blocks;

        blocks.Add(ReceiptInfoLine(_localization["Order.Fields.TotalAmount"], Money(currency, order.TotalAmount), bold: true));
        blocks.Add(ReceiptInfoLine(_localization["Order.Fields.Downpayment"], Money(currency, order.TotalDownpayment)));
        // Show the actually-received deposit only when a card surcharge made it differ
        // from the nominal deposit, so cash/e-transfer receipts stay uncluttered.
        if (order.ReceivedDownpayment != order.TotalDownpayment)
            blocks.Add(ReceiptInfoLine(_localization["Order.Fields.ReceivedDownpayment"], Money(currency, order.ReceivedDownpayment)));
        blocks.Add(ReceiptInfoLine(_localization["Order.Fields.ReceivedFinalBalance"], Money(currency, order.ReceivedFinalBalance)));
        // Name the tax for what it is. On a tax-inclusive order this figure was never added to the
        // total — it was carved out of it — and a receipt reading "tax paid" beside a total that
        // already contained it invites the reader to add them together. There it also names WHICH tax
        // and at what rate ("Includes VAT (6%)"), which is the question the person holding this piece
        // of paper actually asks. Exclusive orders keep "received tax": the line above it is the
        // amount that was added, so nothing needs explaining.
        if (order.TotalTax > 0m)
        {
            blocks.Add(ReceiptInfoLine(
                order.PricesIncludeTax ? TaxLabelConverter.Label(order) : _localization["Order.Fields.PaidTax"],
                Money(currency, order.TotalTax)));
        }
        // AddReceiptTotals runs for every order regardless of refund status (full parity
        // with the on-screen detail panel), so Order.Fields.FinalBalance is always shown here too.
        blocks.Add(ReceiptInfoLine(_localization["Order.Fields.FinalBalance"], Money(currency, order.FinalBalance)));
        var balanceStatusText = new OrderPaymentSummaryConverter().Convert(order, typeof(string), "Status", CultureInfo.CurrentCulture) as string;
        blocks.Add(ReceiptStatusLine(_localization["Order.Fields.BalanceStatus"],
            balanceStatusText, BalanceStatusBrush(order.PaymentStatusKind)));

        document.Blocks.Add(card);

        // The breakdown and the notes sit OUTSIDE the totals panel: they are explanation, and
        // folding them in would dilute the block the eye is meant to land on.
        AddReceiptPaymentNarrative(document, order);

        if (!string.IsNullOrWhiteSpace(order.Notes))
        {
            document.Blocks.Add(ReceiptSectionTitle(_localization["Order.Fields.Notes"]));
            document.Blocks.Add(ReceiptMultilineParagraph(order.Notes));
        }
    }

    /// <summary>
    /// Either how the order was paid, or — for a cancelled/returned one, where a payment-method
    /// breakdown means nothing — why it was refunded. Matches the on-screen detail panel.
    /// </summary>
    private void AddReceiptPaymentNarrative(FlowDocument document, Order order)
    {
        if (order.IsRefunded)
        {
            var reasonLabelKey = order.Status == OrderStatus.Cancelled
                ? "Order.Fields.CancelReason"
                : "Order.Fields.ReturnReason";
            document.Blocks.Add(ReceiptSectionTitle(_localization[reasonLabelKey]));
            document.Blocks.Add(ReceiptMultilineParagraph(
                ReturnReasonSummaryConverter.Resolve(order.StatusReasonCategory, order.StatusReason)));
            return;
        }

        var paymentBreakdown = new OrderPaymentSummaryConverter().Convert(order, typeof(string), null, CultureInfo.CurrentCulture) as string;
        if (string.IsNullOrWhiteSpace(paymentBreakdown) || paymentBreakdown == "-")
            return;

        document.Blocks.Add(ReceiptSectionTitle(_localization["Order.Fields.PaymentBreakdown"]));
        document.Blocks.Add(ReceiptMultilineParagraph(paymentBreakdown));
    }

    private static string Money(CurrencyType currency, decimal value)
        => CurrencySettingService.Format(value, currency);

    // Prepends the preset logo + rich header and appends the rich footer for the
    // current language, so printed receipts share the same branding as the
    // measurements export.
    /// <param name="insertTaxNumber">
    /// Whether this document still needs the tax registration line put in at the top. False when the
    /// caller has already placed it — the receipt does so inside its own generated letterhead, under
    /// the shop's contact details.
    /// </param>
    private static void InjectReceiptBranding(
        FlowDocument document, ReceiptBrandingSettings settings, LocalizedBranding branding, bool insertTaxNumber)
    {
        // Inserted BEFORE the header is prepended, so the header ends up above it: the registration
        // number reads as part of the letterhead, directly under the header.
        //
        // Skipped when the caller has already placed it. The receipt's generated letterhead — shop
        // name, subtitle, contact details — is itself at the top of the document, so inserting here
        // put the tax number ABOVE the shop's own name, reading as though the number were the
        // letterhead.
        if (insertTaxNumber)
        {
            var taxNumberBlock = CreateTaxNumberBlock(ResolveTaxRegistrationNumber(settings));
            if (taxNumberBlock is not null)
                InsertAtTop(document, taxNumberBlock);
        }

        BrandingRenderer.AppendToFlowDocument(document, branding.HeaderXaml, atTop: true);

        var logoBlock = BrandingRenderer.CreateLogoBlock(ReceiptBrandingStore.GetLogoPath(settings), maxHeight: 80, settings.LogoPlacement);
        if (logoBlock is not null)
            InsertAtTop(document, logoBlock);

        BrandingRenderer.AppendToFlowDocument(document, branding.FooterXaml, atTop: false);
    }

    private static void InsertAtTop(FlowDocument document, Block block)
    {
        var anchor = document.Blocks.FirstBlock;
        if (anchor is null)
            document.Blocks.Add(block);
        else
            document.Blocks.InsertBefore(anchor, block);
    }

    /// <summary>
    /// Which tax registration number the receipt prints: the one from the header/footer editor if
    /// it has one, otherwise the shop's own.
    /// </summary>
    /// <remarks>
    /// The header/footer editor WINS. Both are "the shop's number", but the shop setting is the
    /// business-wide fact while the branding entry is a deliberate, per-installation override typed
    /// into the receipt designer itself — so a value there is the more specific instruction and
    /// should not be silently ignored in favour of the general one.
    /// </remarks>
    private static string? ResolveTaxRegistrationNumber(ReceiptBrandingSettings settings)
        => ReceiptBrandingStore.ResolveTaxRegistrationNumber(settings);

    /// <summary>
    /// The tax-number line, or null when neither the shop nor the header/footer editor has a number.
    /// The whole line shape comes from the string table so the separator is translated too — zh uses
    /// a fullwidth colon where en uses ": " — and the NAME of the number comes from the shop's tax
    /// jurisdiction, so a receipt printed in Osaka does not announce a Canadian GST/HST number.
    /// </summary>
    /// <remarks>
    /// Left aligned with the rest of the letterhead. At 11pt it sits just under the body text: it is
    /// a legal detail rather than something to read first, but a tax slip whose registration number
    /// cannot be read is not a tax slip.
    /// </remarks>
    private static Paragraph? CreateTaxNumberBlock(string? taxRegistrationNumber)
    {
        if (string.IsNullOrWhiteSpace(taxRegistrationNumber))
            return null;

        var text = LocalizationService.Instance.Format("Receipt.TaxNumberLine",
            TaxJurisdictions.TaxNumberName(ShopContext.Instance.Current, LocalizationService.Instance),
            taxRegistrationNumber.Trim());

        return new Paragraph(new Run(text))
        {
            FontSize = 11,
            TextAlignment = TextAlignment.Left,
            Foreground = System.Windows.Media.Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 10)
        };
    }

    // Builds a paragraph that preserves the line breaks in multi-line receipt content.
    private static Paragraph ReceiptMultilineParagraph(string content)
    {
        var paragraph = new Paragraph { FontSize = 11, Margin = new Thickness(0, 0, 0, 4) };
        var lines = content.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
                paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new Run(lines[i]));
        }
        return paragraph;
    }

    private static Paragraph ReceiptInfoLine(string label, string? value, bool bold = false)
    {
        // 3px of leading rather than 1: at 12pt the old spacing ran the lines together, which is
        // what made the receipt look like a wall rather than a list.
        var paragraph = new Paragraph { Margin = new Thickness(0, 3, 0, 3) };
        paragraph.Inlines.Add(new Run($"{label}: ") { Foreground = ReceiptLabelBrush });
        var valueRun = new Run(value ?? string.Empty);
        if (bold)
            valueRun.FontWeight = FontWeights.Bold;
        paragraph.Inlines.Add(valueRun);
        return paragraph;
    }

    private static void AddReceiptInfoLineIfHasValue(BlockCollection blocks, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        blocks.Add(ReceiptInfoLine(label, value.Trim()));
    }

    // Balance-status line whose value is coloured by status: green / light green /
    // orange / red (settled-picked-up / settled-not-picked-up / outstanding / refunded).
    private static Paragraph ReceiptStatusLine(string label, string? value, System.Windows.Media.Brush valueBrush)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0, 1, 0, 1) };
        paragraph.Inlines.Add(new Run($"{label}: ") { Foreground = System.Windows.Media.Brushes.Gray });
        paragraph.Inlines.Add(new Run(value ?? string.Empty) { Foreground = valueBrush, FontWeight = FontWeights.SemiBold });
        return paragraph;
    }

    private static System.Windows.Media.Brush BalanceStatusBrush(BalanceStatusKind status)
        => status switch
        {
            BalanceStatusKind.ClearedPickedUp => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2E, 0x7D, 0x32)),   // green
            BalanceStatusKind.ClearedNotPickedUp => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x66, 0xBB, 0x6A)), // light green
            BalanceStatusKind.Refunded => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC6, 0x28, 0x28)),           // red
            _ => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEF, 0x6C, 0x00))                                      // orange
        };

    // --- Receipt chrome -------------------------------------------------------------------------
    // The printed receipt follows the same palette as the application. Declared here as frozen
    // brushes rather than inline, so a colour change is one edit and the printer never re-renders
    // an unfrozen brush per paragraph.
    private static readonly System.Windows.Media.Brush ReceiptAccentBrush = FrozenBrush(0x4F, 0x46, 0xE5);
    private static readonly System.Windows.Media.Brush ReceiptRuleBrush = FrozenBrush(0xE5, 0xE7, 0xEB);
    private static readonly System.Windows.Media.Brush ReceiptCardBrush = FrozenBrush(0xF9, 0xFA, 0xFB);
    private static readonly System.Windows.Media.Brush ReceiptTotalsBrush = FrozenBrush(0xEE, 0xF2, 0xFF);
    private static readonly System.Windows.Media.Brush ReceiptLabelBrush = FrozenBrush(0x6B, 0x72, 0x80);

    private static System.Windows.Media.Brush FrozenBrush(byte r, byte g, byte b)
    {
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// A padded block that groups related lines, so the receipt reads as a few panels rather than
    /// one uninterrupted column of text. Padding is generous on purpose: a printed page has no
    /// hover or spacing cues, so whitespace is the only grouping the reader gets.
    /// </summary>
    private static Section ReceiptCard(System.Windows.Media.Brush background, double topBorder = 1)
        => new()
        {
            Margin = new Thickness(0, 0, 0, 14),
            Padding = new Thickness(14, 11, 14, 11),
            Background = background,
            BorderBrush = ReceiptRuleBrush,
            BorderThickness = new Thickness(1, topBorder, 1, 1)
        };

    private static Paragraph ReceiptSectionTitle(string title)
        => new(new Bold(new Run(title)))
        {
            FontSize = 13.5,
            Foreground = ReceiptAccentBrush,
            Margin = new Thickness(0, 10, 0, 6)
        };

    private static Paragraph ReceiptDivider()
        => new()
        {
            Margin = new Thickness(0, 10, 0, 10),
            BorderBrush = ReceiptRuleBrush,
            BorderThickness = new Thickness(0, 0, 0, 1)
        };

    // A lighter, thinner divider placed after each service section (including the last).
    private static Paragraph ReceiptServiceDivider()
        => new()
        {
            Margin = new Thickness(0, 8, 0, 8),
            BorderBrush = ReceiptRuleBrush,
            BorderThickness = new Thickness(0, 0, 0, 0.7)
        };

    private string LocalizeWithFallback(string prefix, string? suffix)
    {
        if (string.IsNullOrWhiteSpace(suffix))
            return string.Empty;

        var key = $"{prefix}.{suffix}";
        var localized = _localization[key];
        return string.Equals(localized, key, StringComparison.Ordinal) ? suffix : localized;
    }
}

// Attached properties that let each GridViewColumn declare the Order member it sorts by
// (SortKey) and carry the current sort-direction arrow (SortGlyph) shown in its header.
public static class OrderColumnSort
{
    public static readonly DependencyProperty SortKeyProperty =
        DependencyProperty.RegisterAttached(
            "SortKey", typeof(string), typeof(OrderColumnSort), new PropertyMetadata(string.Empty));

    public static void SetSortKey(DependencyObject element, string value) => element.SetValue(SortKeyProperty, value);

    public static string GetSortKey(DependencyObject element) => (string)element.GetValue(SortKeyProperty);

    public static readonly DependencyProperty SortGlyphProperty =
        DependencyProperty.RegisterAttached(
            "SortGlyph", typeof(string), typeof(OrderColumnSort), new PropertyMetadata(string.Empty));

    public static void SetSortGlyph(DependencyObject element, string value) => element.SetValue(SortGlyphProperty, value);

    public static string GetSortGlyph(DependencyObject element) => (string)element.GetValue(SortGlyphProperty);
}
