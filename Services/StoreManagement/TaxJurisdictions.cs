using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CameywareOrder.Configuration;
using CameywareOrder.Localization;
using CameywareOrder.Models;

namespace CameywareOrder.Services;

/// <summary>
/// The one answer to "what tax does a store in this location charge, and is it quoted inclusive".
/// Loads the shipped presets from <c>Settings/System/Defaults/tax-jurisdictions.json</c> once and
/// caches them.
/// </summary>
/// <remarks>
/// Deliberately shaped like <see cref="ShopCurrencies"/>: a bounded, shipped set the UI reads to seed
/// a shop, with a hard fallback so a missing or corrupt file can never leave the app unable to
/// price anything. The presets are DATA, not code, for the same reason the language tables are —
/// adding a market, or fixing a rate a government changed, is editing a file, not shipping a build.
///
/// Reading is defensive on purpose: this runs during startup and every time Shop Settings opens, so
/// a malformed file degrades to the built-in fallback rather than throwing into a window that does
/// not exist yet.
/// </remarks>
public static class TaxJurisdictions
{
    /// <summary>Home market: the location a fresh install and an un-located shop assume.</summary>
    /// <remarks>
    /// A COUNTRY code. Canada ships as one entry rather than one per province — see the shipped file's
    /// <c>regionComment</c> — but nothing here requires that: a code is free-form, so re-adding
    /// <c>CA-ON</c> is a line of JSON and a language key, and <see cref="For"/> keeps a shop already
    /// stored under a regional code resolving to its country in the meantime.
    /// </remarks>
    public const string DefaultCode = "CA";

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private static IReadOnlyList<TaxJurisdiction>? _cached;
    private static string _defaultCode = DefaultCode;

    /// <summary>
    /// Every jurisdiction the build ships, in file order. Loaded once and cached; a build with no
    /// readable presets file falls back to a single home-market entry so pricing still works.
    /// </summary>
    public static IReadOnlyList<TaxJurisdiction> All => _cached ??= Load();

    /// <summary>The home-market jurisdiction — the file's declared default, or the first entry.</summary>
    /// <remarks>
    /// Forces <see cref="All"/> BEFORE reading <c>_defaultCode</c>. Written the other way round
    /// (<c>Find(_defaultCode) ?? All[0]</c>) the field was read while it still held the compile-time
    /// constant, because only <see cref="Load"/> resolves it to whatever the file declares — so the
    /// very first call answered differently from every later one. Invisible today only because the
    /// shipped file happens to declare the same code as the constant.
    /// </remarks>
    public static TaxJurisdiction Default
    {
        get
        {
            var all = All;
            return Find(_defaultCode) ?? all[0];
        }
    }

    /// <summary>
    /// The jurisdiction for a stored location code, or null when the code is blank or no longer
    /// shipped. Null is a legitimate "never located" state, exactly like an unset language code.
    /// Strict on purpose: <see cref="For"/> is the one place a regional code is widened to its
    /// country.
    /// </summary>
    public static TaxJurisdiction? Find(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        return All.FirstOrDefault(j => string.Equals(j.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The jurisdiction a shop sits in: its own code, else the COUNTRY that code names, else the home
    /// market.
    /// </summary>
    /// <remarks>
    /// The country step is what lets the shipped list change shape without stranding anybody. Canada
    /// was three provincial entries and is now one country entry, so every shop already stored as
    /// <c>CA-ON</c>, <c>CA-AB</c> or <c>CA-BC</c> holds a code this build no longer ships; matching it
    /// to <c>CA</c> keeps it explicitly Canadian instead of leaving it to land on the home market by
    /// luck — which is the same answer today only because the home market IS Canada, and would quietly
    /// become a wrong answer the moment that changed. It runs the other way too: a shop stored as
    /// <c>CA-ON</c> resolves straight back to Ontario the day an Ontario row is added again, so the
    /// province it recorded is preserved rather than rewritten.
    ///
    /// Deliberately NOT folded into <see cref="Find"/>, whose null answer means "not shipped" and is
    /// what the settings screen relies on to tell a live code from a dead one.
    /// </remarks>
    public static TaxJurisdiction For(Shop? shop)
        => Find(shop?.LocationCode) ?? FindCountry(shop?.LocationCode) ?? Default;

    /// <summary>
    /// The country entry a regional code sits in — <c>CA-ON</c> → <c>CA</c> — or null when the code
    /// names no region or its country is not shipped either.
    /// </summary>
    private static TaxJurisdiction? FindCountry(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        var trimmed = code.Trim();
        var separator = trimmed.IndexOf('-');

        return separator > 0 ? Find(trimmed[..separator]) : null;
    }

    /// <summary>
    /// Whether a shop's prices are quoted tax-inclusive. Drives both the money split and whether the
    /// order editor shows the payment/tax panel at all. Defaults to the home market (exclusive) for
    /// an un-located shop, so no existing branch changes until it picks an inclusive location.
    /// </summary>
    public static bool PricesIncludeTax(Shop? shop) => For(shop).PricesIncludeTax;

    /// <summary>
    /// The rate a tax-INCLUSIVE shop's embedded tax is backed out at: the jurisdiction's standard
    /// rate, with nothing per payment method. A value-added tax is a property of the sale, so it
    /// cannot differ between a cash and a card settlement of the same price — which is exactly what
    /// reading it from <c>PaymentTaxRules</c> would have made it do.
    /// </summary>
    public static decimal IncludedTaxRatePercent(Shop? shop) => For(shop).StandardRatePercent;

    /// <summary>
    /// Whether to ask this shop for a tax registration number at all — only where its location
    /// issues one. See <see cref="TaxJurisdiction.TaxNumberLabel"/> for why this is declared per
    /// jurisdiction rather than inferred from the pricing mode.
    /// </summary>
    public static bool CollectsTaxNumber(Shop? shop) => For(shop).CollectsTaxNumber;

    /// <summary>What this shop's tax number is called, in the current language.</summary>
    public static string TaxNumberName(Shop? shop, LocalizationService localization)
        => For(shop).TaxNumberName(localization);

    /// <summary>
    /// What this shop's location calls the TAX ITSELF (a <c>TaxName.*</c> value), for the line that
    /// tells a customer the price already contains it.
    /// </summary>
    public static string TaxName(Shop? shop, LocalizationService localization)
        => For(shop).TaxName(localization);

    /// <summary>The key naming it, for a document rendered in a language other than the UI's.</summary>
    public static string TaxNumberKey(Shop? shop) => For(shop).TaxNumberKey;

    /// <summary>The best-guess location for an existing shop that has never been located: from its currency.</summary>
    public static string LocationForCurrency(CurrencyType currency)
        => All.FirstOrDefault(j => j.DefaultCurrency == currency)?.Code ?? DefaultCode;

    private static IReadOnlyList<TaxJurisdiction> Load()
    {
        try
        {
            var path = SystemSettingsPaths.TaxJurisdictionsFile;
            if (!File.Exists(path))
                return Fallback;

            var payload = JsonSerializer.Deserialize<JurisdictionsPayload>(File.ReadAllText(path), ReadOptions);
            var entries = payload?.Jurisdictions;
            if (entries is null || entries.Count == 0)
                return Fallback;

            var parsed = entries
                .Where(e => !string.IsNullOrWhiteSpace(e.Code)
                            && Enum.TryParse<CurrencyType>(e.DefaultCurrency, ignoreCase: true, out _))
                .Select(e => new TaxJurisdiction(
                    e.Code!.Trim(),
                    e.StandardRatePercent,
                    e.Inclusive,
                    Enum.Parse<CurrencyType>(e.DefaultCurrency!, ignoreCase: true),
                    string.IsNullOrWhiteSpace(e.TaxNumberLabel) ? null : e.TaxNumberLabel.Trim(),
                    string.IsNullOrWhiteSpace(e.TaxNameLabel) ? null : e.TaxNameLabel.Trim()))
                .ToList();

            if (parsed.Count == 0)
                return Fallback;

            if (!string.IsNullOrWhiteSpace(payload!.DefaultLocationCode)
                && parsed.Exists(j => string.Equals(j.Code, payload.DefaultLocationCode, StringComparison.OrdinalIgnoreCase)))
            {
                _defaultCode = payload.DefaultLocationCode!.Trim();
            }

            return parsed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Fallback;
        }
    }

    /// <summary>Built-in home market, used only when the shipped file is missing or unreadable.</summary>
    /// <remarks>
    /// Quotes NO standard rate, matching the shipped Canadian entries: sales tax is added separately at
    /// settlement here, so the rate is the shop's to enter rather than something this build asserts. It
    /// must agree with the file it stands in for — a fallback carrying 13% would have a corrupt presets
    /// file seed a matrix the intact one leaves tax free, which is the one situation nobody would think
    /// to check.
    /// </remarks>
    private static IReadOnlyList<TaxJurisdiction> Fallback { get; } = new[]
    {
        new TaxJurisdiction(DefaultCode, standardRatePercent: 0m, pricesIncludeTax: false,
            CurrencyType.CAD, taxNumberLabel: "GstHst")
    };

    private sealed record JurisdictionsPayload(string? DefaultLocationCode, List<JurisdictionEntry>? Jurisdictions);

    /// <summary>
    /// One row of the shipped file. <c>Inclusive</c> is named for the JSON explicitly rather than
    /// after the model property it fills, because a nested <c>PricesIncludeTax</c> would shadow the
    /// enclosing class's method of that name (S3218) — two different things one identifier away from
    /// each other is worth avoiding here whatever the analyzer thinks.
    /// </summary>
    private sealed record JurisdictionEntry(
        string? Code,
        decimal StandardRatePercent,
        [property: JsonPropertyName("pricesIncludeTax")] bool Inclusive,
        string? DefaultCurrency,
        string? TaxNumberLabel,
        string? TaxNameLabel);
}
