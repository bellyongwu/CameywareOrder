using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System.Diagnostics;
using System.Globalization;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
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

        InitializeLanguageSwitcher();
        ApplyRolePermissions();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
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
    /// necessarily correct after 切换店铺. Everything here is hidden rather than disabled — a dead
    /// control invites a support call, an absent one reads as "not offered".
    /// </remarks>
    private void ApplyRolePermissions()
    {
        var auth = AuthenticationService.Instance;

        // Non-administrators run in the language their shop is configured for, so a branch's staff
        // all see the same thing.
        var language = Show(auth.CanChooseLanguage);
        LanguageSwitchLabel.Visibility = language;
        LanguageSwitchBox.Visibility = language;

        // A manager configures the shop they run; staff take orders in it.
        var configure = Show(auth.CanConfigureShop);
        ShopSettingsMenuItem.Visibility = configure;
        MeasurementTermsMenuItem.Visibility = configure;
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

        // Greeted by NAME where there is one — an account name is what you sign in with, not what
        // anybody calls you. The role shown is the one held in the OPEN shop, because that is the
        // one the surrounding chrome has just been gated by.
        var who = string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserName : user.DisplayName;
        var role = UserPresentation.RoleText(_localization, auth.CurrentRole);
        GreetingText.Text = _localization.Format("Main.Greeting", who, role);
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
        _viewModel.Detach();

        base.OnClosed(e);
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

    private void InitializeLanguageSwitcher()
    {
        _isLanguageSwitchInitializing = true;
        LanguageSwitchBox.ItemsSource = _localization.AvailableLanguages;
        LanguageSwitchBox.DisplayMemberPath = nameof(LanguageOption.Name);
        LanguageSwitchBox.SelectedValuePath = nameof(LanguageOption.Code);
        LanguageSwitchBox.SelectedValue = _localization.CurrentLanguageCode;
        _isLanguageSwitchInitializing = false;
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
        }
        finally
        {
            _suppressLanguageRefresh = false;
        }
    }

    // Defence in depth on every handler below the 本地数据库 and 导入/导出 menus: those menus are
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

    private void OnOrderRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.SelectedOrder is null)
            return;

        OnEditOrderClick(sender, new RoutedEventArgs());
    }

    // Requirement 4a: pressing Enter on a selected order opens the same edit
    // window as a double-click. Pressing Delete triggers the delete action.
    private void OnOrderRowKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel.SelectedOrder is null)
            return;

        switch (e.Key)
        {
            case Key.Enter:
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

    // Right-clicking a row selects it first so context-menu actions operate on the
    // intended order (WPF does not select on right-click by default).
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static",
        Justification = "Named from XAML (EventSetter Handler=\"OnOrderRowRightClick\"). The generated " +
                        "InitializeComponent wires it as this.OnOrderRowRightClick, which does not compile " +
                        "against a static method.")]
    private void OnOrderRowRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListViewItem item)
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
        if (_viewModel.SelectedOrder is null) return;
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

        AddMeasurementSections(document, order, languageCode, isInch, pageBreakBefore: false);

        InjectReceiptBranding(document, brandingSettings, branding);

        return document;
    }

    // Renders the measurement content into an existing document. When pageBreakBefore is
    // true (receipt + measurements) the first block starts on a fresh page.
    private void AddMeasurementSections(FlowDocument document, Order order, string languageCode, bool isInch, bool pageBreakBefore)
    {
        var title = ReceiptSectionTitle(_localization.GetText("Customer.Measurements.PrintTitle", languageCode));
        if (pageBreakBefore)
            title.BreakPageBefore = true;
        document.Blocks.Add(title);

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

    // --- Shops (本地配置 → 切换店铺 / 店铺设置) ----------------------------------

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

    private void OnUserManagementClick(object sender, RoutedEventArgs e)
    {
        // Defence in depth: the menu item is hidden for non-administrators, but the check belongs
        // where the action happens too.
        if (!AuthenticationService.Instance.CanManageUsers)
            return;

        new UserManagementWindow(_localization, _scopeFactory) { Owner = this }.ShowDialog();

        // An administrator can revoke their own access to the open shop here. Their capabilities in
        // it are resolved from the assignments that were just rewritten, so the chrome has to be
        // re-gated even though the shop itself did not change.
        ApplyRolePermissions();
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

    // --- Import / export (本地配置 → 导入/导出) ----------------------------------

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
        var symbol = CurrencySettingService.Instance.Symbol;

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

        AddAlterationReceiptSection(document, order, symbol);
        AddClothingReceiptSection(document, order, symbol);
        AddCustomMadeReceiptSection(document, order, symbol);

        AddReceiptTotals(document, order, symbol);

        InjectReceiptBranding(document, brandingSettings, branding);

        return document;
    }

    // The default shop title only appears when the header editor has no content.
    private void AddReceiptTitle(FlowDocument document, bool hasHeader)
    {
        if (hasHeader)
            return;

        // The receipt is headed with the SHOP's own name, not a fixed app title — each branch
        // prints under its own name. Falls back to Main.HeaderTitle when no shop is open.
        document.Blocks.Add(new Paragraph(new Bold(new Run(ShopContext.Instance.CurrentName)))
        {
            FontSize = 18,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 2)
        });
        document.Blocks.Add(new Paragraph(new Run(_localization["Receipt.Title"]))
        {
            TextAlignment = TextAlignment.Center,
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(0, 0, 0, 12)
        });
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
        blocks.Add(ReceiptInfoLine(_localization["Order.Fields.OrderDate"], order.OrderDate.ToLocalTime().ToString("yyyy-MM-dd HH:mm")));
        blocks.Add(ReceiptInfoLine(_localization["Order.Fields.Status"], _localization[$"Status.{order.Status}"]));
        blocks.Add(ReceiptInfoLine(_localization["Order.Fields.CurrencyType"], _localization[$"CurrencyType.{CurrencySettingService.Instance.Current}"]));
        var servicesSummary = new OrderServicesSummaryConverter().Convert(order, typeof(string), null, CultureInfo.CurrentCulture) as string;
        AddReceiptInfoLineIfHasValue(blocks, _localization["Order.Fields.ServiceType"], servicesSummary);

        document.Blocks.Add(card);
    }

    // Alterations service detail. Only shown when the section carries a charge and a
    // deposit method has been selected; otherwise the service is considered not added.
    private void AddAlterationReceiptSection(FlowDocument document, Order order, string symbol)
    {
        if (!order.AlterationAddedToReceipt)
            return;

        document.Blocks.Add(ReceiptSectionTitle(_localization["OrderEdit.Panel.Alterations"]));
        if (!string.IsNullOrWhiteSpace(order.ServiceDetails))
            document.Blocks.Add(new Paragraph(new Run(LocalizeWithFallback("Alteration.Category", order.ServiceDetails))) { Margin = new Thickness(0, 0, 0, 4) });

        document.Blocks.Add(ReceiptInfoLine(_localization["Order.Fields.Subtotal"], Money(symbol, order.AlterationSubtotal ?? 0m)));
        if (order.AlterationTax > 0m)
            document.Blocks.Add(ReceiptInfoLine(_localization["Order.Fields.TaxAmount"], Money(symbol, order.AlterationTax)));
        document.Blocks.Add(ReceiptInfoLine(_localization["Receipt.SectionTotal"], Money(symbol, order.AlterationTotal), bold: true));
        document.Blocks.Add(ReceiptServiceDivider());
    }

    // Ready-made clothing / accessories. Only shown when the section carries a charge and a
    // deposit method has been selected; otherwise the service is considered not added.
    private void AddClothingReceiptSection(FlowDocument document, Order order, string symbol)
    {
        if (order.Items.Count == 0 || !order.ClothingAddedToReceipt)
            return;

        document.Blocks.Add(ReceiptSectionTitle(_localization["OrderEdit.Panel.ReadyMade"]));
        foreach (var item in order.Items)
        {
            var line = new Paragraph { Margin = new Thickness(0, 0, 0, 2) };
            var name = LocalizeWithFallback("ClothingItem", item.ProductName);
            line.Inlines.Add(new Run($"{name}  {Money(symbol, item.EffectiveUnitPrice)} x{item.Quantity}"));
            line.Inlines.Add(new Run($"    {Money(symbol, item.TotalPrice)}") { FontWeight = FontWeights.SemiBold });
            document.Blocks.Add(line);
        }
        document.Blocks.Add(ReceiptInfoLine(_localization["Order.Fields.Subtotal"], Money(symbol, order.ClothingSubtotal ?? 0m)));
        if (order.ClothingTax > 0m)
            document.Blocks.Add(ReceiptInfoLine(_localization["Order.Fields.TaxAmount"], Money(symbol, order.ClothingTax)));
        document.Blocks.Add(ReceiptInfoLine(_localization["Receipt.SectionTotal"], Money(symbol, order.ClothingTotal), bold: true));
        document.Blocks.Add(ReceiptServiceDivider());
    }

    // Custom-made records. Only shown when the section carries a charge and a deposit
    // method has been selected; otherwise the service is considered not added.
    private void AddCustomMadeReceiptSection(FlowDocument document, Order order, string symbol)
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
            line.Inlines.Add(new Run($"    {Money(symbol, record.SumTotal)}") { FontWeight = FontWeights.SemiBold });
            document.Blocks.Add(line);
        }
        document.Blocks.Add(ReceiptInfoLine(_localization["Receipt.SectionTotal"], Money(symbol, order.CustomMadeTotal), bold: true));
        document.Blocks.Add(ReceiptServiceDivider());
    }

    /// <summary>
    /// The money the customer actually cares about, in its own tinted panel with a heavier top
    /// rule — on a printed page this is the block people look at first, and it should not have to
    /// be found among the service lines above it.
    /// </summary>
    private void AddReceiptTotals(FlowDocument document, Order order, string symbol)
    {
        var card = ReceiptCard(ReceiptTotalsBrush, topBorder: 2);
        var blocks = card.Blocks;

        blocks.Add(ReceiptInfoLine(_localization["Order.Fields.TotalAmount"], Money(symbol, order.TotalAmount), bold: true));
        blocks.Add(ReceiptInfoLine(_localization["Order.Fields.Downpayment"], Money(symbol, order.TotalDownpayment)));
        // Show the actually-received deposit only when a card surcharge made it differ
        // from the nominal deposit, so cash/e-transfer receipts stay uncluttered.
        if (order.ReceivedDownpayment != order.TotalDownpayment)
            blocks.Add(ReceiptInfoLine(_localization["Order.Fields.ReceivedDownpayment"], Money(symbol, order.ReceivedDownpayment)));
        blocks.Add(ReceiptInfoLine(_localization["Order.Fields.ReceivedFinalBalance"], Money(symbol, order.ReceivedFinalBalance)));
        if (order.TotalTax > 0m)
            blocks.Add(ReceiptInfoLine(_localization["Order.Fields.PaidTax"], Money(symbol, order.TotalTax)));
        // AddReceiptTotals runs for every order regardless of refund status (full parity
        // with the on-screen detail panel), so 剩余尾款 is always shown here too.
        blocks.Add(ReceiptInfoLine(_localization["Order.Fields.FinalBalance"], Money(symbol, order.FinalBalance)));
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

    private static string Money(string symbol, decimal value) => $"{symbol}{value:N2}";

    // Prepends the preset logo + rich header and appends the rich footer for the
    // current language, so printed receipts share the same branding as the
    // measurements export.
    private static void InjectReceiptBranding(FlowDocument document, ReceiptBrandingSettings settings, LocalizedBranding branding)
    {
        // Inserted BEFORE the header is prepended, so the header ends up above it: the shop's tax
        // registration number reads as part of the letterhead, directly under the header — which
        // is what makes the receipt usable as a tax slip.
        var taxNumberBlock = CreateTaxNumberBlock(settings.TaxRegistrationNumber);
        if (taxNumberBlock is not null)
            InsertAtTop(document, taxNumberBlock);

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
    /// The GST/HST line, or null when the shop has not entered a number (本地配置 →
    /// 添加或更改页眉页脚). The whole line shape comes from the string table so the separator is
    /// translated too — zh uses a fullwidth colon where en uses ": ".
    /// </summary>
    private static Paragraph? CreateTaxNumberBlock(string? taxRegistrationNumber)
    {
        if (string.IsNullOrWhiteSpace(taxRegistrationNumber))
            return null;

        var text = LocalizationService.Instance.Format("Receipt.TaxNumberLine", taxRegistrationNumber.Trim());

        return new Paragraph(new Run(text))
        {
            FontSize = 11,
            TextAlignment = TextAlignment.Center,
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
