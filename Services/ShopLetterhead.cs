using CameywareOrder.Localization;

namespace CameywareOrder.Services;

/// <summary>One labelled contact line — "Address: 4300 Steeles Ave East", already localized.</summary>
public sealed record ShopLetterheadLine(string Label, string Value);

/// <summary>
/// The letterhead the application generates when the header/footer editor has not supplied one:
/// the shop's name, what the document is, how to reach the shop, and its GST/HST number.
/// </summary>
/// <remarks>
/// Plain resolved strings rather than blocks or spans, because two different renderers consume it —
/// the printed FlowDocument and the QuestPDF export. It exists because they had drifted: the receipt
/// grew a proper letterhead while both measurement paths kept injecting the tax registration number
/// at the very top of the page, so a measurements sheet opened with a bare
/// "GST/HST 税号：..." above its title and never named the shop at all.
///
/// Every string is resolved for an explicitly passed language. The measurement sheet is produced in
/// the language chosen in the print dialog, which is not necessarily the UI language.
/// </remarks>
public sealed class ShopLetterhead
{
    /// <summary>The SHOP's own name — each branch prints under its own, not a fixed app title.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>What this document is: "收据" on a receipt, "量体打印单" on a measurements sheet.</summary>
    public string? Subtitle { get; init; }

    public IReadOnlyList<ShopLetterheadLine> ContactLines { get; init; } = [];

    /// <summary>The finished GST/HST line, or null when no number is configured anywhere.</summary>
    public string? TaxLine { get; init; }

    /// <summary>
    /// Builds the letterhead for the open shop. <paramref name="subtitleKey"/> names the document.
    /// </summary>
    public static ShopLetterhead Build(LocalizationService localization, string languageCode, string subtitleKey)
    {
        ArgumentNullException.ThrowIfNull(localization);

        var shop = ShopContext.Instance.Current;
        var taxNumber = ReceiptBrandingStore.ResolveTaxRegistrationNumber(ReceiptBrandingStore.Load());

        var candidates = new (string LabelKey, string? Value)[]
        {
            ("Shop.Setup.Address", shop?.ResolveAddress(languageCode)),
            ("Shop.Setup.Phone", shop?.PhoneNumber),
            ("Shop.Setup.Email", shop?.Email),
            ("Shop.Setup.Website", shop?.Website),
        };

        return new ShopLetterhead
        {
            Name = ShopContext.Instance.CurrentName,
            Subtitle = localization.GetText(subtitleKey, languageCode),
            ContactLines = candidates
                .Where(line => !string.IsNullOrWhiteSpace(line.Value))
                .Select(line => new ShopLetterheadLine(
                    localization.GetText(line.LabelKey, languageCode), line.Value!.Trim()))
                .ToList(),
            TaxLine = string.IsNullOrWhiteSpace(taxNumber)
                ? null
                : string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    localization.GetText("Receipt.TaxNumberLine", languageCode),
                    taxNumber.Trim()),
        };
    }
}
