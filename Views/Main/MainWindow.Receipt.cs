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

using CameywareOrder.Converters;

namespace CameywareOrder;

public partial class MainWindow
{
    // Building the printed receipt as a FlowDocument: the letterhead, the customer block, one section per service actually added, the totals and the payment narrative. Every figure is read off the order's own money accessors, never recomputed here.

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
