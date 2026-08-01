using System.Globalization;
using CameywareOrder.Localization;
using CameywareOrder.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CameywareOrder.Services;

/// <summary>A caption over a figure — one cell of the report's headline strip.</summary>
public sealed record SettlementMetric(string Caption, string Value);

/// <summary>One row of a report table.</summary>
public sealed record SettlementRow(string Label, IReadOnlyList<string> Values);

/// <summary>A titled block of rows, optionally with a bold total line under it.</summary>
public sealed record SettlementSection(
    string Title,
    IReadOnlyList<string> Columns,
    IReadOnlyList<SettlementRow> Rows)
{
    public SettlementRow? Total { get; init; }
}

/// <summary>
/// Everything the settlement PDF prints — already localized, already formatted.
/// </summary>
/// <remarks>
/// Plain strings and bytes, with no string keys and no <c>Order</c>: the sheet can be produced in a
/// language the application is not running in, so the composer must not look anything up. Exactly
/// the shape <c>MeasurementSheetContent</c> takes, and for the same reason — a layout that can only
/// be checked by a human clicking Export is a layout whose regressions ship.
/// </remarks>
public sealed record SettlementContent
{
    public string Title { get; init; } = string.Empty;

    /// <summary>The period, spelled the way the heading should read it.</summary>
    public string Period { get; init; } = string.Empty;

    public string GeneratedOn { get; init; } = string.Empty;

    /// <summary>Shown in place of every section when the period held no orders.</summary>
    public string? EmptyMessage { get; init; }

    public IReadOnlyList<SettlementMetric> Metrics { get; init; } = [];

    public IReadOnlyList<SettlementSection> Sections { get; init; } = [];

    /// <summary>PNGs of the on-screen charts — see <c>ChartImage</c> for why they arrive as bytes.</summary>
    public byte[]? ServiceChart { get; init; }

    public byte[]? MethodChart { get; init; }

    public string? ServiceChartTitle { get; init; }

    public string? MethodChartTitle { get; init; }

    /// <summary>Shop name, contact lines and tax number — printed only when no custom header exists.</summary>
    public ShopLetterhead? Letterhead { get; init; }

    public string? HeaderXaml { get; init; }
    public string? FooterXaml { get; init; }
    public byte[]? LogoBytes { get; init; }
    public LogoPlacement LogoPlacement { get; init; } = LogoPlacement.Left;

    /// <summary>
    /// Turns a computed report into printable content, in the language of <paramref name="text"/>.
    /// </summary>
    /// <remarks>
    /// The CHARTS are not built here — they are WPF elements and this has to stay headless. The
    /// caller renders them and adds them with <c>with</c>.
    /// </remarks>
    public static SettlementContent Build(
        SettlementReport report,
        ILocalizedText text,
        CultureInfo culture,
        string languageCode,
        Func<decimal, string> money,
        Func<ServiceLine, string> lineName,
        Func<PaymentMethod, string> methodName)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(money);

        return new SettlementContent
        {
            Title = text["Settlement.Title"],
            Period = report.Period.Title(text, culture),
            GeneratedOn = text.Format(
                "Settlement.GeneratedOn", DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
            EmptyMessage = report.IsEmpty ? text["Settlement.Empty"] : null,
            Metrics =
            [
                new(text["Settlement.PreTax"], money(report.PreTaxTotal)),
                new(text["Settlement.Tax"], money(report.TaxTotal)),
                new(text["Settlement.PostTax"], money(report.PostTaxTotal)),
                new(text["Settlement.Received"], money(report.ReceivedTotal)),
                new(text["Settlement.Outstanding"], money(report.OutstandingTotal)),
                new(text["Settlement.RefundedValue"], money(report.RefundedValue))
            ],
            Sections =
            [
                ServiceSection(report, text, money, lineName),
                OrderSection(report, text),
                PaymentSection(report, text, money, methodName)
            ],
            ServiceChartTitle = text["Settlement.Chart.ByService"],
            MethodChartTitle = text["Settlement.Chart.ByMethod"],
            Letterhead = ShopLetterhead.Build(LocalizationService.Instance, languageCode, "Settlement.Title")
        };
    }

    private static SettlementSection ServiceSection(
        SettlementReport report, ILocalizedText text, Func<decimal, string> money, Func<ServiceLine, string> lineName)
        => new(
            text["Settlement.Section.Services"],
            [
                text["Settlement.OrderCount"].Replace("{0}", string.Empty).Trim(),
                text["Settlement.PreTax"], text["Settlement.Tax"], text["Settlement.PostTax"],
                text["Settlement.Received"], text["Settlement.Outstanding"]
            ],
            report.Lines.Select(line => new SettlementRow(
                lineName(line.Line),
                [
                    line.OrderCount.ToString(CultureInfo.InvariantCulture),
                    money(line.PreTax), money(line.Tax), money(line.PostTax),
                    money(line.Received), money(line.Outstanding)
                ])).ToList())
        {
            Total = new SettlementRow(
                text["Settlement.Section.Revenue"],
                [
                    report.Counts.Earning.ToString(CultureInfo.InvariantCulture),
                    money(report.PreTaxTotal), money(report.TaxTotal), money(report.PostTaxTotal),
                    money(report.ReceivedTotal), money(report.OutstandingTotal)
                ])
        };

    private static SettlementSection OrderSection(SettlementReport report, ILocalizedText text)
    {
        var counts = report.Counts;
        string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

        return new SettlementSection(
            text["Settlement.Section.Orders"],
            [string.Empty],
            [
                new(text["Main.Records"], [Count(counts.Total)]),
                new(text["Settlement.Orders.Unfinished"], [Count(counts.Unfinished)]),
                new(text["Status.Completed"], [Count(counts.Completed)]),
                new(text["Status.Shipped"], [Count(counts.Shipped)]),
                new(text["Status.Cancelled"], [Count(counts.Cancelled)]),
                new(text["Status.Returned"], [Count(counts.Returned)])
            ]);
    }

    private static SettlementSection PaymentSection(
        SettlementReport report, ILocalizedText text, Func<decimal, string> money, Func<PaymentMethod, string> methodName)
        => new(
            text["Settlement.Section.Payments"],
            [string.Empty],
            report.Methods
                .Select(method => new SettlementRow(methodName(method.Method), [money(method.Received)]))
                .ToList())
        {
            Total = new SettlementRow(text["Settlement.Received"], [money(report.ReceivedTotal)])
        };
}

/// <summary>
/// Lays out the settlement PDF.
/// </summary>
/// <remarks>
/// Headless, like <c>MeasurementSheetDocument</c>, and built the same way so the two documents look
/// like they came from the same shop: the same letterhead, the same branded header and footer, the
/// same rule under the masthead.
/// </remarks>
public static class SettlementDocument
{
    private const string AccentColor = "#4F46E5";
    private const string RuleColor = "#D8DEE6";
    private const string MutedColor = "#6B7280";
    private const string CardColor = "#F3F4F6";
    private const string HeadColor = "#9CA3AF";

    public static void Save(SettlementContent content, string filePath)
        => Compose(content).GeneratePdf(filePath);

    public static IDocument Compose(SettlementContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        // Set per document rather than once at startup, exactly as MeasurementSheetDocument does:
        // QuestPDF throws on the first Create() without it, and a report generated from a harness
        // or a background path never runs the application's startup.
        QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(34);
                page.DefaultTextStyle(style => style.FontSize(9.5f).FontFamily(Fonts.SegoeUI));

                page.Header().Element(header => ComposeHeader(header, content));
                page.Footer().Element(footer => ComposeFooter(footer, content));

                page.Content().PaddingVertical(12).Column(column =>
                {
                    column.Spacing(14);

                    column.Item().Text(content.Period).Bold().FontSize(16).FontColor(AccentColor);

                    if (content.EmptyMessage is { Length: > 0 } empty)
                    {
                        column.Item().PaddingTop(30).AlignCenter().Text(empty).FontColor(MutedColor);
                        return;
                    }

                    AddMetrics(column, content.Metrics);
                    AddCharts(column, content);

                    foreach (var section in content.Sections)
                        AddSection(column, section);
                });
            });
        });
    }

    /// <summary>The headline figures, as a strip of tinted cards.</summary>
    private static void AddMetrics(ColumnDescriptor column, IReadOnlyList<SettlementMetric> metrics)
    {
        if (metrics.Count == 0)
            return;

        column.Item().Row(row =>
        {
            row.Spacing(6);
            foreach (var metric in metrics)
            {
                row.RelativeItem().Background(CardColor).Padding(8).Column(cell =>
                {
                    cell.Item().Text(metric.Caption).FontSize(7.5f).FontColor(MutedColor);
                    cell.Item().PaddingTop(2).Text(metric.Value).Bold().FontSize(11.5f);
                });
            }
        });
    }

    private static void AddCharts(ColumnDescriptor column, SettlementContent content)
    {
        if (content.ServiceChart is null && content.MethodChart is null)
            return;

        column.Item().Row(row =>
        {
            row.Spacing(10);
            AddChart(row, content.ServiceChartTitle, content.ServiceChart);
            AddChart(row, content.MethodChartTitle, content.MethodChart);
        });
    }

    private static void AddChart(RowDescriptor row, string? title, byte[]? image)
    {
        if (image is null)
            return;

        row.RelativeItem().Border(0.8f).BorderColor(RuleColor).Padding(8).Column(cell =>
        {
            if (title is { Length: > 0 })
                cell.Item().PaddingBottom(4).Text(title).Bold().FontSize(9).FontColor(AccentColor);

            cell.Item().Image(image).FitWidth();
        });
    }

    private static void AddSection(ColumnDescriptor column, SettlementSection section)
    {
        column.Item().Column(block =>
        {
            block.Item().PaddingBottom(5).Text(section.Title).Bold().FontSize(11).FontColor(AccentColor);

            block.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(2.2f);
                    for (var i = 0; i < section.Columns.Count; i++)
                        columns.RelativeColumn();
                });

                // Only worth a header row when the columns are actually named — the orders and
                // payments blocks are label/value pairs and a blank strip above them reads as a
                // rendering fault.
                if (section.Columns.Any(name => name.Length > 0))
                {
                    table.Header(header =>
                    {
                        header.Cell().Element(HeadCell).Text(string.Empty);
                        foreach (var name in section.Columns)
                            header.Cell().Element(HeadCell).AlignRight().Text(name);
                    });
                }

                foreach (var row in section.Rows)
                {
                    table.Cell().Element(BodyCell).Text(row.Label);
                    foreach (var value in row.Values)
                        table.Cell().Element(BodyCell).AlignRight().Text(value);
                }

                if (section.Total is { } total)
                {
                    table.Cell().Element(TotalCell).Text(total.Label).Bold();
                    foreach (var value in total.Values)
                        table.Cell().Element(TotalCell).AlignRight().Text(value).Bold();
                }
            });
        });
    }

    private static IContainer HeadCell(IContainer container)
        => container.PaddingVertical(3).BorderBottom(0.8f).BorderColor(RuleColor)
            .DefaultTextStyle(style => style.FontSize(7.5f).FontColor(HeadColor));

    private static IContainer BodyCell(IContainer container)
        => container.PaddingVertical(3.5f).BorderBottom(0.4f).BorderColor("#EEF1F5");

    private static IContainer TotalCell(IContainer container)
        => container.PaddingTop(5).BorderTop(1.2f).BorderColor("#111827");

    /// <summary>Logo, then either the branded header or the generated letterhead, then a rule.</summary>
    private static void ComposeHeader(IContainer container, SettlementContent content)
    {
        container.Column(column =>
        {
            column.Spacing(4);

            if (content.LogoBytes is not null)
            {
                BrandingRenderer.AlignLogo(column.Item(), content.LogoPlacement)
                    .MaxHeight(52).Image(content.LogoBytes);
            }

            // A custom header REPLACES the generated letterhead rather than adding to it — the same
            // rule the receipt and the measurements sheet follow.
            if (!BrandingRenderer.IsEmpty(content.HeaderXaml))
                BrandingRenderer.RenderToPdf(column, content.HeaderXaml);
            else
                AddLetterhead(column, content.Letterhead, content.Title);

            column.Item().PaddingTop(5).LineHorizontal(0.8f).LineColor(RuleColor);
        });
    }

    private static void AddLetterhead(ColumnDescriptor column, ShopLetterhead? letterhead, string title)
    {
        if (letterhead is null)
        {
            column.Item().Text(title).Bold().FontSize(15).FontColor(AccentColor);
            return;
        }

        column.Item().Text(letterhead.Name).Bold().FontSize(15).FontColor(AccentColor);

        if (letterhead.Subtitle is { Length: > 0 } subtitle)
            column.Item().Text(subtitle).FontSize(10.5f).FontColor(MutedColor);

        foreach (var line in letterhead.ContactLines)
            column.Item().Text($"{line.Label}: {line.Value}").FontSize(8).FontColor(MutedColor);

        if (letterhead.TaxLine is { Length: > 0 } taxLine)
            column.Item().Text(taxLine).FontSize(8).FontColor(MutedColor);
    }

    private static void ComposeFooter(IContainer container, SettlementContent content)
    {
        container.Column(column =>
        {
            if (!BrandingRenderer.IsEmpty(content.FooterXaml))
            {
                column.Item().PaddingBottom(4);
                BrandingRenderer.RenderToPdf(column, content.FooterXaml);
            }

            column.Item().LineHorizontal(0.6f).LineColor(RuleColor);
            column.Item().PaddingTop(3).Row(row =>
            {
                row.RelativeItem().Text(content.GeneratedOn).FontSize(7.5f).FontColor(HeadColor);
                row.AutoItem().Text(pageText =>
                {
                    pageText.DefaultTextStyle(style => style.FontSize(7.5f).FontColor(HeadColor));
                    pageText.CurrentPageNumber();
                    pageText.Span(" / ");
                    pageText.TotalPages();
                });
            });
        });
    }
}
