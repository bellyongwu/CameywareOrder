using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System.Diagnostics;
using System.Globalization;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using LeeYongeOrdering.Converters;
using LeeYongeOrdering.Data;
using LeeYongeOrdering.Localization;
using LeeYongeOrdering.Models;
using LeeYongeOrdering.Services;
using LeeYongeOrdering.ViewModels;
using LeeYongeOrdering.Views;

namespace LeeYongeOrdering;

public partial class MainWindow : Window
{
    private static readonly string ExplorerPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

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
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _localization.LanguageChanged += OnLanguageChangedGlobally;
        RefreshToolbarLabels();
        _ = _viewModel.LoadOrdersAsync();
    }

    private static bool IsReadOnlyStatus(OrderStatus status)
        => status is OrderStatus.Completed or OrderStatus.Cancelled or OrderStatus.Returned;

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

    private void OnOpenDataFolderClick(object sender, RoutedEventArgs e)
    {
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
    private void OnOrderRowRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListViewItem item)
            item.IsSelected = true;
    }

    // Keeps the trailing (Notes) column filling the remaining width as the list resizes.
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
            PagePadding = new Thickness(40),
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
        AddReceiptInfoLineIfHasValue(document,
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
        var window = new ReceiptBrandingWindow(_localization) { Owner = this };
        window.ShowDialog();
    }

    private void OnCurrencySettingClick(object sender, RoutedEventArgs e)
    {
        var window = new CurrencySettingWindow(_localization) { Owner = this };
        if (window.ShowDialog() == true)
            _viewModel.LoadOrdersCommand.Execute(null);
    }

    private void OnMeasurementTermsClick(object sender, RoutedEventArgs e)
    {
        var window = new MeasurementTermsWindow { Owner = this };
        window.ShowDialog();
    }

    // --- Import / export (本地配置 → 导入/导出) ----------------------------------

    private void OnExportMeasurementTermsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            FileName = "measurement-terms.json",
            Filter = "JSON (*.json)|*.json"
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
        var dialog = new OpenFileDialog
        {
            Filter = "JSON (*.json)|*.json",
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
        var dialog = new SaveFileDialog
        {
            FileName = "orders-backup.db",
            Filter = "SQLite Database (*.db)|*.db"
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
        var dialog = new OpenFileDialog
        {
            Filter = "SQLite Database (*.db)|*.db",
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

    private FlowDocument BuildReceiptDocument(Order order, double pageWidth)
    {
        var symbol = CurrencySettingService.Instance.Symbol;

        var document = new FlowDocument
        {
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = 12,
            PagePadding = new Thickness(40),
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

        document.Blocks.Add(new Paragraph(new Bold(new Run(_localization["Main.HeaderTitle"])))
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

    private void AddReceiptCustomerInfo(FlowDocument document, Order order)
    {
        document.Blocks.Add(ReceiptInfoLine(_localization["Order.Fields.OrderNumber"], order.OrderNumber));
        AddReceiptInfoLineIfHasValue(document, _localization["Order.Fields.CustomerName"], order.CustomerName);
        AddReceiptInfoLineIfHasValue(document, _localization["Order.Fields.PhoneNumber"], order.PhoneNumber);
        AddReceiptInfoLineIfHasValue(document, _localization["Order.Fields.Email"], order.Email);
        AddReceiptInfoLineIfHasValue(document, _localization["Order.Fields.Address"], order.Address);
        document.Blocks.Add(ReceiptInfoLine(_localization["Order.Fields.OrderDate"], order.OrderDate.ToLocalTime().ToString("yyyy-MM-dd HH:mm")));
        document.Blocks.Add(ReceiptInfoLine(_localization["Order.Fields.Status"], _localization[$"Status.{order.Status}"]));
        document.Blocks.Add(ReceiptInfoLine(_localization["Order.Fields.CurrencyType"], _localization[$"CurrencyType.{CurrencySettingService.Instance.Current}"]));
        var servicesSummary = new OrderServicesSummaryConverter().Convert(order, typeof(string), null, CultureInfo.CurrentCulture) as string;
        AddReceiptInfoLineIfHasValue(document, _localization["Order.Fields.ServiceType"], servicesSummary);
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

    private void AddReceiptTotals(FlowDocument document, Order order, string symbol)
    {
        document.Blocks.Add(ReceiptInfoLine(_localization["Order.Fields.TotalAmount"], Money(symbol, order.TotalAmount), bold: true));
        document.Blocks.Add(ReceiptInfoLine(_localization["Order.Fields.Downpayment"], Money(symbol, order.TotalDownpayment)));
        // Show the actually-received deposit only when a card surcharge made it differ
        // from the nominal deposit, so cash/e-transfer receipts stay uncluttered.
        if (order.ReceivedDownpayment != order.TotalDownpayment)
            document.Blocks.Add(ReceiptInfoLine(_localization["Order.Fields.ReceivedDownpayment"], Money(symbol, order.ReceivedDownpayment)));
        document.Blocks.Add(ReceiptInfoLine(_localization["Order.Fields.ReceivedFinalBalance"], Money(symbol, order.ReceivedFinalBalance)));
        if (order.TotalTax > 0m)
            document.Blocks.Add(ReceiptInfoLine(_localization["Order.Fields.PaidTax"], Money(symbol, order.TotalTax)));
        // A refunded (cancelled/returned) order no longer owes a final balance, so the
        // 剩余尾款 line is omitted from its receipt.
        if (!order.IsRefunded)
            document.Blocks.Add(ReceiptInfoLine(_localization["Order.Fields.FinalBalance"], Money(symbol, order.FinalBalance)));
        var balanceStatusText = new OrderPaymentSummaryConverter().Convert(order, typeof(string), "Status", CultureInfo.CurrentCulture) as string;
        document.Blocks.Add(ReceiptStatusLine(_localization["Order.Fields.BalanceStatus"],
            balanceStatusText, BalanceStatusBrush(order.PaymentStatusKind)));

        var paymentBreakdown = new OrderPaymentSummaryConverter().Convert(order, typeof(string), null, CultureInfo.CurrentCulture) as string;
        if (!string.IsNullOrWhiteSpace(paymentBreakdown) && paymentBreakdown != "-")
        {
            document.Blocks.Add(ReceiptSectionTitle(_localization["Order.Fields.PaymentBreakdown"]));
            document.Blocks.Add(ReceiptMultilineParagraph(paymentBreakdown));
        }

        if (!string.IsNullOrWhiteSpace(order.Notes))
        {
            document.Blocks.Add(ReceiptSectionTitle(_localization["Order.Fields.Notes"]));
            document.Blocks.Add(ReceiptMultilineParagraph(order.Notes));
        }
    }

    private static string Money(string symbol, decimal value) => $"{symbol}{value:N2}";

    // Prepends the preset logo + rich header and appends the rich footer for the
    // current language, so printed receipts share the same branding as the
    // measurements export.
    private static void InjectReceiptBranding(FlowDocument document, ReceiptBrandingSettings settings, LocalizedBranding branding)
    {
        BrandingRenderer.AppendToFlowDocument(document, branding.HeaderXaml, atTop: true);

        var logoBlock = BrandingRenderer.CreateLogoBlock(ReceiptBrandingStore.GetLogoPath(settings), maxHeight: 80, settings.LogoPlacement);
        if (logoBlock is not null)
        {
            var anchor = document.Blocks.FirstBlock;
            if (anchor is null)
                document.Blocks.Add(logoBlock);
            else
                document.Blocks.InsertBefore(anchor, logoBlock);
        }

        BrandingRenderer.AppendToFlowDocument(document, branding.FooterXaml, atTop: false);
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
        var paragraph = new Paragraph { Margin = new Thickness(0, 1, 0, 1) };
        paragraph.Inlines.Add(new Run($"{label}: ") { Foreground = System.Windows.Media.Brushes.Gray });
        var valueRun = new Run(value ?? string.Empty);
        if (bold)
            valueRun.FontWeight = FontWeights.Bold;
        paragraph.Inlines.Add(valueRun);
        return paragraph;
    }

    private static void AddReceiptInfoLineIfHasValue(FlowDocument document, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        document.Blocks.Add(ReceiptInfoLine(label, value.Trim()));
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

    private static Paragraph ReceiptSectionTitle(string title)
        => new(new Bold(new Run(title))) { FontSize = 14, Margin = new Thickness(0, 6, 0, 4) };

    private static Paragraph ReceiptDivider()
        => new()
        {
            Margin = new Thickness(0, 6, 0, 6),
            BorderBrush = System.Windows.Media.Brushes.LightGray,
            BorderThickness = new Thickness(0, 0, 0, 1)
        };

    // A lighter, thinner divider placed after each service section (including the last).
    private static Paragraph ReceiptServiceDivider()
        => new()
        {
            Margin = new Thickness(0, 4, 0, 4),
            BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE6, 0xE6, 0xE6)),
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
