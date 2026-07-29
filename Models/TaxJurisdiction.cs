using System.Globalization;
using CameywareOrder.Localization;

namespace CameywareOrder.Models;

/// <summary>
/// One tax jurisdiction preset: the standard rate a store in this location charges, whether its
/// prices are quoted tax-inclusive, the currency it trades in, and what it calls the tax number it
/// issues to a business. Loaded from <c>Settings/System/Defaults/tax-jurisdictions.json</c> by
/// <c>TaxJurisdictions</c>.
/// </summary>
/// <remarks>
/// A PRESET, not a rule the app enforces: picking a location seeds a shop's payment-tax rules and
/// pricing mode from these values, and the shop may override any of them afterwards. The preset
/// lives in shipped data rather than code so a rate change — which happens on a government's
/// schedule, not this project's — is a one-line file edit, not a build.
///
/// Tax is anchored on LOCATION because that is what tax law is a function of. Language, currency and
/// jurisdiction are three different things — a shop running in Simplified Chinese may sit in Canada —
/// so modelling tax on the store's location keeps them from being conflated the way deriving
/// everything from the language once did.
///
/// <see cref="StandardRatePercent"/> is not decoration: in a tax-INCLUSIVE location it is the ONLY
/// rate in play, because a value-added tax cannot vary by payment method. It reached shipped data
/// once and was read nowhere for those locations, leaving inclusive shops taxed at whatever the
/// per-method matrix happened to hold. A preset nothing reads is indistinguishable from a wrong one.
/// </remarks>
public sealed class TaxJurisdiction
{
    public TaxJurisdiction(
        string code,
        decimal standardRatePercent,
        bool pricesIncludeTax,
        CurrencyType defaultCurrency,
        string? taxNumberLabel = null)
    {
        Code = code;
        StandardRatePercent = standardRatePercent;
        PricesIncludeTax = pricesIncludeTax;
        DefaultCurrency = defaultCurrency;
        TaxNumberLabel = taxNumberLabel;
    }

    /// <summary>Stable location code, e.g. "CA-ON", "CN", "JP". Stored on <see cref="Shop.LocationCode"/>.</summary>
    public string Code { get; }

    /// <summary>
    /// The standard rate for this location. In a tax-EXCLUSIVE location it is the rate every payment
    /// method is seeded with and the shop may then override per method. In a tax-INCLUSIVE one it is
    /// the rate outright: the per-method matrix is not consulted at all there.
    /// </summary>
    public decimal StandardRatePercent { get; }

    /// <summary>
    /// True in VAT / consumption-tax markets (China, Japan, the EU) where prices are quoted with the
    /// tax already in them: the money split backs the tax out of the price rather than adding it on
    /// top, and Shop Settings replaces its per-method matrix with this jurisdiction's single rate,
    /// because a value-added tax does not vary by how a sale is settled. False in Canada and the US,
    /// where tax is added at settlement — the behaviour every existing shop already has.
    /// </summary>
    public bool PricesIncludeTax { get; }

    /// <summary>
    /// The currency a shop in this location trades in. Used to infer a location for shops that
    /// predate the setting (see <c>App.BackfillShopLocationsAsync</c>); it does NOT constrain or
    /// change what a shop prices in, which stays a matter for <c>ShopCurrencies</c>.
    /// </summary>
    public CurrencyType DefaultCurrency { get; }

    /// <summary>
    /// Which tax number this jurisdiction issues to a business, as the suffix of a
    /// <c>TaxNumber.&lt;name&gt;</c> key — <c>GstHst</c>, <c>Vat</c>, <c>ChinaTaxpayer</c>,
    /// <c>JapanInvoice</c>. Null where the jurisdiction issues none, and the field is then not asked
    /// for at all.
    /// </summary>
    /// <remarks>
    /// Grouped by tax REGIME rather than by jurisdiction, because that is the real relationship:
    /// Ontario, Alberta and British Columbia share one GST/HST number, France and Spain each issue an
    /// EU VAT number. Naming it here rather than in five language files is the same rule as the rate —
    /// "GST/HST" used to be spelled into the label, the hint and the receipt line in every language,
    /// so a shop in Osaka read <c>GST/HST</c> on its own tax slip.
    ///
    /// **Do not infer this from <see cref="PricesIncludeTax"/>.** Canada's GST/HST is a consumption
    /// tax quoted tax-EXCLUSIVE, so "does this market tax consumption" and "does it quote prices
    /// inclusive of that tax" have different answers, and inferring one from the other would drop the
    /// number from the home market.
    /// </remarks>
    public string? TaxNumberLabel { get; }

    /// <summary>Whether a business here is issued a tax number worth asking for and printing.</summary>
    public bool CollectsTaxNumber => !string.IsNullOrWhiteSpace(TaxNumberLabel);

    /// <summary>
    /// The string-table key naming this tax number. Falls back to the generic name, so a number
    /// already stored by a shop that has since moved somewhere issuing none is still printed with an
    /// honest label rather than silently dropped. Exposed as a KEY as well as a resolved string
    /// because a printed document can be rendered in a language other than the one on screen.
    /// </summary>
    public string TaxNumberKey => CollectsTaxNumber ? $"TaxNumber.{TaxNumberLabel}" : "TaxNumber.Generic";

    /// <summary>What to call the tax number, in the current language.</summary>
    public string TaxNumberName(LocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(localization);
        return localization[TaxNumberKey];
    }

    /// <summary>
    /// Localized display name, from the language file's <c>TaxJurisdiction.&lt;code&gt;</c> key.
    /// </summary>
    /// <remarks>
    /// The key's value is a FORMAT string carrying the tax's local name and punctuation
    /// ("Canada — Ontario (HST {0}%)"), with the rate filled in from <see cref="StandardRatePercent"/>
    /// — the whole reason the presets are editable without a rebuild is defeated if the rate is also
    /// spelled out in five language files, where editing the JSON would leave it stale. A jurisdiction
    /// with no single rate to quote (the US) simply omits the placeholder.
    /// </remarks>
    public string DisplayName(LocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(localization);
        return localization.Format($"TaxJurisdiction.{Code}", StandardRatePercent.ToString("0.##", CultureInfo.CurrentCulture));
    }
}
