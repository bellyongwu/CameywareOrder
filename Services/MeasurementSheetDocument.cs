using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CameywareOrder.Services;

/// <summary>One measured value: the term's name and what the tailor wrote against it.</summary>
public sealed record MeasurementSheetRow(string Label, string Value);

/// <summary>One garment's worth of measurements, printed under its own heading.</summary>
public sealed record MeasurementSheetSection(string Title, IReadOnlyList<MeasurementSheetRow> Rows);

/// <summary>
/// Everything the measurements PDF prints, already localized.
/// </summary>
/// <remarks>
/// Deliberately free of any string key: the sheet is generated in the language chosen in the print
/// dialog, which need not be the UI language, so localizing here would quietly use the wrong one.
/// The caller resolves every string while it still knows which language was asked for.
/// </remarks>
public sealed class MeasurementSheetContent
{
    /// <summary>Fallback title, used only when the branding header does not supply one.</summary>
    public string? Title { get; init; }

    /// <summary>Order number, customer, unit — who and what the sheet is for.</summary>
    public IReadOnlyList<MeasurementSheetRow> InfoRows { get; init; } = [];

    public IReadOnlyList<MeasurementSheetSection> Sections { get; init; } = [];

    /// <summary>The finished GST/HST line, or null when no number is configured.</summary>
    public string? TaxLine { get; init; }

    public string? HeaderXaml { get; init; }
    public string? FooterXaml { get; init; }
    public byte[]? LogoBytes { get; init; }
    public LogoPlacement LogoPlacement { get; init; } = LogoPlacement.Left;
}

/// <summary>
/// Lays out the custom-made measurements sheet.
/// </summary>
/// <remarks>
/// Separate from <c>CustomMadeServiceWindow</c> because the window cannot be opened without a WPF
/// message loop, and a print layout that can only be checked by a human clicking Export is a print
/// layout whose regressions ship. Given plain data, this renders headlessly.
/// </remarks>
public static class MeasurementSheetDocument
{
    // Matches the shell: #4F46E5 is the accent used throughout the application.
    private const string AccentColor = "#4F46E5";
    private const string BodyColor = "#111827";
    private const string LabelColor = "#6B7280";
    private const string RuleColor = "#D8DCE4";
    private const string CardColor = "#F5F6FA";

    // The two row tints must be far enough apart to be seen. They started a single step apart
    // (#F6F7FB / #FAFAFC) and the table rendered as one flat grey block with no stripes at all.
    private const string RowColor = "#F7F8FC";
    private const string StripeColor = "#EDEFF7";

    /// <summary>
    /// Width of the label column in the garment tables. Generous on purpose: a term name runs about
    /// a quarter longer in French than in English, and a wrapped label costs more than a wide gap.
    /// </summary>
    private const float TermColumnWidth = 190;

    /// <summary>
    /// The info card's labels are field names rather than term names — short in every language — so
    /// they get a narrower column. At the term width the values floated a third of the page away
    /// from the labels they belong to.
    /// </summary>
    private const float InfoColumnWidth = 132;

    public static void Save(MeasurementSheetContent content, string filePath)
        => Compose(content).GeneratePdf(filePath);

    public static IDocument Compose(MeasurementSheetContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        QuestPDF.Settings.License = LicenseType.Community;
        var hasBrandedHeader = !BrandingRenderer.IsEmpty(content.HeaderXaml);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(34);
                page.DefaultTextStyle(text => text.FontSize(10.5f).FontColor(BodyColor));

                // The page's own header/footer slots, NOT Content. Rendered as content they appeared
                // once and stopped: a sheet that ran to a second page carried the letterhead on page
                // one only, and put the footer wherever the last garment happened to end. These
                // slots repeat on every page, which is the whole point of a letterhead.
                page.Header().Element(header => ComposeHeader(header, content));
                page.Footer().Element(footer => ComposeFooter(footer, content.FooterXaml));

                page.Content().PaddingVertical(14).Column(column =>
                {
                    column.Spacing(14);

                    if (!hasBrandedHeader && !string.IsNullOrWhiteSpace(content.Title))
                        column.Item().Text(content.Title).Bold().FontSize(17).FontColor(AccentColor);

                    AddInfoCard(column, content.InfoRows);

                    foreach (var section in content.Sections)
                        AddSection(column, section);
                });
            });
        });
    }

    /// <summary>Logo, branded header, tax registration line, and a rule closing the letterhead.</summary>
    private static void ComposeHeader(IContainer container, MeasurementSheetContent content)
    {
        container.Column(column =>
        {
            column.Spacing(4);

            if (content.LogoBytes is not null)
            {
                BrandingRenderer.AlignLogo(column.Item(), content.LogoPlacement)
                    .MaxHeight(58).Image(content.LogoBytes);
            }

            BrandingRenderer.RenderToPdf(column, content.HeaderXaml);

            if (!string.IsNullOrWhiteSpace(content.TaxLine))
                column.Item().Text(content.TaxLine).FontSize(9f).FontColor(LabelColor);

            column.Item().PaddingTop(6).LineHorizontal(0.8f).LineColor(RuleColor);
        });
    }

    /// <summary>The branded footer, then the page number on its own line.</summary>
    private static void ComposeFooter(IContainer container, string? footerXaml)
    {
        container.Column(column =>
        {
            column.Spacing(4);
            column.Item().PaddingBottom(2).LineHorizontal(0.8f).LineColor(RuleColor);

            BrandingRenderer.RenderToPdf(column, footerXaml);

            // Printed even on a one-page sheet: cheaper than a reader holding a loose page and
            // wondering whether it is the whole document.
            column.Item().AlignCenter().Text(text =>
            {
                text.DefaultTextStyle(style => style.FontSize(8.5f).FontColor(Colors.Grey.Medium));
                text.CurrentPageNumber();
                text.Span(" / ");
                text.TotalPages();
            });
        });
    }

    /// <summary>
    /// Who and what the sheet is for, grouped into one tinted panel so the measurements below read
    /// as a separate thing — the same device the printed receipt uses for its order details.
    /// </summary>
    private static void AddInfoCard(ColumnDescriptor column, IReadOnlyList<MeasurementSheetRow> infoRows)
    {
        if (infoRows.Count == 0)
            return;

        column.Item()
            .Background(CardColor)
            .Border(0.8f).BorderColor(RuleColor)
            .Padding(12)
            .Column(card =>
            {
                card.Spacing(3);

                foreach (var row in infoRows)
                {
                    card.Item().Row(line =>
                    {
                        // The colon belongs to the LABEL. It used to lead the value (": 9051234567"),
                        // which reads as though the field name had gone missing.
                        line.ConstantItem(InfoColumnWidth).Text($"{row.Label}:").FontColor(LabelColor);
                        line.RelativeItem().Text(row.Value).SemiBold();
                    });
                }
            });
    }

    private static void AddSection(ColumnDescriptor column, MeasurementSheetSection section)
    {
        if (section.Rows.Count == 0)
            return;

        column.Item().Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(TermColumnWidth);
                columns.RelativeColumn();
            });

            // The garment name is the table's HEADER, not a heading above it, so that a garment
            // whose measurements straddle a page break is named again on the continuation page.
            // It was a plain item above the table, and rendering a two-page sheet showed the
            // consequence: the heading sat at the foot of page one and four unlabelled numbers
            // opened page two. Wrapping both in a single Column item does not make them atomic —
            // a Column splits across pages like anything else.
            table.Header(header =>
            {
                header.Cell().ColumnSpan(2)
                    .BorderLeft(3).BorderColor(AccentColor)
                    .PaddingLeft(8).PaddingVertical(3).PaddingBottom(8)
                    .Text(section.Title).Bold().FontSize(12.5f).FontColor(AccentColor);
            });

            // Striped rather than ruled: the sheet is read across, and on a long list of
            // near-identical numbers the eye loses the row it was on.
            for (var i = 0; i < section.Rows.Count; i++)
            {
                var row = section.Rows[i];
                var background = i % 2 == 1 ? StripeColor : RowColor;

                table.Cell().Background(background).PaddingVertical(3).PaddingLeft(8)
                    .Text(row.Label).FontColor(LabelColor);
                table.Cell().Background(background).PaddingVertical(3).PaddingRight(8)
                    .Text(row.Value).SemiBold();
            }
        });
    }
}
