using Microsoft.Extensions.DependencyInjection;
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
        EditOrderButton.Content = _localization[isReadOnly ? "Toolbar.ViewOrder" : "Toolbar.EditOrder"];
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
        if (sender is DataGridRow row)
            row.IsSelected = true;
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

    private FlowDocument BuildReceiptDocument(Order order, double pageWidth)
    {
        var symbol = order.CurrencyType == CurrencyType.CNY ? "￥" : "$";
        string Money(decimal value) => $"{symbol}{value:N2}";

        var document = new FlowDocument
        {
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = 12,
            PagePadding = new Thickness(40),
            PageWidth = pageWidth,
            ColumnWidth = pageWidth
        };

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

        document.Blocks.Add(ReceiptInfoLine(_localization["Order.Fields.OrderNumber"], order.OrderNumber));
        AddReceiptInfoLineIfHasValue(document, _localization["Order.Fields.CustomerName"], order.CustomerName);
        AddReceiptInfoLineIfHasValue(document, _localization["Order.Fields.PhoneNumber"], order.PhoneNumber);
        AddReceiptInfoLineIfHasValue(document, _localization["Order.Fields.Email"], order.Email);
        AddReceiptInfoLineIfHasValue(document, _localization["Order.Fields.Address"], order.Address);
        document.Blocks.Add(ReceiptInfoLine(_localization["Order.Fields.OrderDate"], order.OrderDate.ToLocalTime().ToString("yyyy-MM-dd HH:mm")));

        document.Blocks.Add(ReceiptDivider());

        // Alterations service detail. Only shown when the section carries a charge and a
        // deposit method has been selected; otherwise the service is considered not added.
        if (order.AlterationAddedToReceipt)
        {
            document.Blocks.Add(ReceiptSectionTitle(_localization["OrderEdit.Panel.Alterations"]));
            if (!string.IsNullOrWhiteSpace(order.ServiceDetails))
                document.Blocks.Add(new Paragraph(new Run(LocalizeWithFallback("Alteration.Category", order.ServiceDetails))) { Margin = new Thickness(0, 0, 0, 4) });
            if (order.AlterationTotal > 0m)
            {
                document.Blocks.Add(ReceiptInfoLine(_localization["Order.Fields.Subtotal"], Money(order.AlterationSubtotal ?? 0m)));
                document.Blocks.Add(ReceiptInfoLine(_localization["Receipt.SectionTotal"], Money(order.AlterationTotal), bold: true));
            }
        }

        // Ready-made clothing / accessories. Only shown when the section carries a charge and a
        // deposit method has been selected; otherwise the service is considered not added.
        if (order.Items.Count > 0 && order.ClothingAddedToReceipt)
        {
            document.Blocks.Add(ReceiptSectionTitle(_localization["OrderEdit.Panel.ReadyMade"]));
            foreach (var item in order.Items)
            {
                var line = new Paragraph { Margin = new Thickness(0, 0, 0, 2) };
                var name = LocalizeWithFallback("ClothingItem", item.ProductName);
                line.Inlines.Add(new Run($"{name}  {Money(item.EffectiveUnitPrice)} x{item.Quantity}"));
                line.Inlines.Add(new Run($"    {Money(item.TotalPrice)}") { FontWeight = FontWeights.SemiBold });
                document.Blocks.Add(line);
            }
            document.Blocks.Add(ReceiptInfoLine(_localization["Order.Fields.Subtotal"], Money(order.ClothingSubtotal ?? 0m)));
            document.Blocks.Add(ReceiptInfoLine(_localization["Receipt.SectionTotal"], Money(order.ClothingTotal), bold: true));
        }

        // Custom-made records. Only shown when the section carries a charge and a deposit
        // method has been selected; otherwise the service is considered not added.
        var customMadeRecords = order.CustomMadeRecords;
        if (customMadeRecords.Count > 0 && order.CustomMadeAddedToReceipt)
        {
            var summaryConverter = new CustomMadeRecordSummaryConverter();
            document.Blocks.Add(ReceiptSectionTitle(_localization["Detail.CustomMadeRecords"]));
            foreach (var record in customMadeRecords)
            {
                var summary = summaryConverter.Convert(record, typeof(string), null, CultureInfo.CurrentCulture) as string ?? string.Empty;
                var line = new Paragraph { Margin = new Thickness(0, 0, 0, 2) };
                line.Inlines.Add(new Run(summary));
                line.Inlines.Add(new Run($"    {Money(record.SumTotal)}") { FontWeight = FontWeights.SemiBold });
                document.Blocks.Add(line);
            }
            document.Blocks.Add(ReceiptInfoLine(_localization["Receipt.SectionTotal"], Money(order.CustomMadeTotal), bold: true));
        }

        document.Blocks.Add(ReceiptDivider());

        document.Blocks.Add(ReceiptInfoLine(_localization["Order.Fields.TotalAmount"], Money(order.TotalAmount), bold: true));
        document.Blocks.Add(ReceiptInfoLine(_localization["Order.Fields.PrepaidDownpayment"], Money(order.TotalDownpayment)));
        document.Blocks.Add(ReceiptInfoLine(_localization["Order.Fields.ReceivedFinalBalance"], Money(order.ReceivedFinalBalance)));
        document.Blocks.Add(ReceiptInfoLine(_localization["Order.Fields.FinalBalance"], Money(order.FinalBalance)));
        document.Blocks.Add(ReceiptInfoLine(_localization["Order.Fields.BalanceStatus"],
            order.IsBalanceCleared ? _localization["Payment.Status.Cleared"] : _localization["Payment.Status.Outstanding"]));

        document.Blocks.Add(new Paragraph(new Run($"{_localization["Receipt.PrintedAt"]}: {DateTime.Now:yyyy-MM-dd HH:mm}"))
        {
            FontSize = 10,
            Foreground = System.Windows.Media.Brushes.Gray,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0)
        });

        return document;
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

    private static Paragraph ReceiptSectionTitle(string title)
        => new(new Bold(new Run(title))) { FontSize = 11, Margin = new Thickness(0, 6, 0, 4) };

    private static Paragraph ReceiptDivider()
        => new()
        {
            Margin = new Thickness(0, 6, 0, 6),
            BorderBrush = System.Windows.Media.Brushes.LightGray,
            BorderThickness = new Thickness(0, 0, 0, 1)
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
