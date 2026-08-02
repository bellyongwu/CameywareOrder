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

namespace CameywareOrder;

public partial class MainWindow
{
    // The print actions themselves: the three menu entries, the measurement sheet, and the pre-print dialog that chooses its language and unit.

    private void OnPrintReceiptClick(object sender, RoutedEventArgs e)
    {
        var order = _viewModel.SelectedOrder;
        if (order is null || !AuthenticationService.Instance.CanPrintOrderDocuments)
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
        if (!AuthenticationService.Instance.CanPrintOrderDocuments)
            return;

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
}
