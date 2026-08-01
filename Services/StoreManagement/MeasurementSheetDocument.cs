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
    /// <summary>
    /// The generated letterhead — shop name, document title, contact lines, GST/HST — printed only
    /// when the header/footer editor has not supplied a header of its own, exactly as on the
    /// receipt. A custom header IS the letterhead and replaces this wholesale.
    /// </summary>
    public ShopLetterhead? Letterhead { get; init; }

    /// <summary>Order number, customer, unit — who and what the sheet is for.</summary>
    public IReadOnlyList<MeasurementSheetRow> InfoRows { get; init; } = [];

    public IReadOnlyList<MeasurementSheetSection> Sections { get; init; } = [];

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

                    // A custom header replaces the generated letterhead, and with it the subtitle
                    // naming the document — so the title moves into the body rather than being
                    // dropped. The printed sheet does exactly the same, which is the point: the two
                    // must not differ in structure depending on whether branding is configured.
                    if (!BrandingRenderer.IsEmpty(content.HeaderXaml)
                        && !string.IsNullOrWhiteSpace(content.Letterhead?.Subtitle))
                    {
                        column.Item().Text(content.Letterhead.Subtitle)
                            .Bold().FontSize(13.5f).FontColor(AccentColor);
                    }

                    AddInfoCard(column, content.InfoRows);

                    foreach (var section in content.Sections)
                        AddSection(column, section);
                });
            });
        });
    }

    /// <summary>Logo, then either the branded header or the generated letterhead, then a rule.</summary>
    private static void ComposeHeader(IContainer container, MeasurementSheetContent content)
    {
        var hasBrandedHeader = !BrandingRenderer.IsEmpty(content.HeaderXaml);

        container.Column(column =>
        {
            column.Spacing(4);

            if (content.LogoBytes is not null)
            {
                BrandingRenderer.AlignLogo(column.Item(), content.LogoPlacement)
                    .MaxHeight(58).Image(content.LogoBytes);
            }

            // A custom header REPLACES the generated letterhead rather than adding to it — a shop
            // that typed its own address into the editor must not have the shop record's address
            // printed underneath it as well. Same rule as the receipt.
            if (hasBrandedHeader)
                BrandingRenderer.RenderToPdf(column, content.HeaderXaml);
            else
                AddGeneratedLetterhead(column, content.Letterhead);

            column.Item().PaddingTop(6).LineHorizontal(0.8f).LineColor(RuleColor);
        });
    }

    /// <summary>
    /// Shop name, what the document is, how to reach the shop, then the GST/HST number — in that
    /// order, matching the printed receipt block for block.
    /// </summary>
    /// <remarks>
    /// The tax line goes LAST. It used to be injected at the very top of the page, which put a bare
    /// bare Receipt.TaxNumberLine above the document's own title and left the sheet never naming the shop.
    /// </remarks>
    private static void AddGeneratedLetterhead(ColumnDescriptor column, ShopLetterhead? letterhead)
    {
        if (letterhead is null)
            return;

        if (!string.IsNullOrWhiteSpace(letterhead.Name))
            column.Item().Text(letterhead.Name).Bold().FontSize(16).FontColor(BodyColor);

        if (!string.IsNullOrWhiteSpace(letterhead.Subtitle))
            column.Item().Text(letterhead.Subtitle).FontSize(11).FontColor(LabelColor);

        foreach (var line in letterhead.ContactLines)
        {
            column.Item().PaddingTop(1)
                .Text($"{line.Label}: {line.Value}").FontSize(9.5f).FontColor(LabelColor);
        }

        if (!string.IsNullOrWhiteSpace(letterhead.TaxLine))
            column.Item().PaddingTop(3).Text(letterhead.TaxLine).FontSize(9f).FontColor(LabelColor);
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
