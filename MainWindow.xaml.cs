using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Globalization;
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
        _localization.LanguageChanged += OnLanguageChangedGlobally;
        _ = _viewModel.LoadOrdersAsync();
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
    // window as a double-click.
    private void OnOrderRowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        if (_viewModel.SelectedOrder is null)
            return;

        e.Handled = true;
        OnEditOrderClick(sender, new RoutedEventArgs());
    }

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
        document.Blocks.Add(ReceiptInfoLine(_localization["Order.Fields.CustomerName"], order.CustomerName));
        document.Blocks.Add(ReceiptInfoLine(_localization["Order.Fields.PhoneNumber"], order.PhoneNumber));
        document.Blocks.Add(ReceiptInfoLine(_localization["Order.Fields.OrderDate"], order.OrderDate.ToLocalTime().ToString("yyyy-MM-dd HH:mm")));

        document.Blocks.Add(ReceiptDivider());

        // Alterations service detail.
        if (!string.IsNullOrWhiteSpace(order.ServiceDetails) || order.AlterationTotal > 0m)
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

        // Ready-made clothing / accessories.
        if (order.Items.Count > 0)
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

        // Custom-made records.
        var customMadeRecords = order.CustomMadeRecords;
        if (customMadeRecords.Count > 0)
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
