using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace CameywareOrder.Models;

/// <summary>
/// One shop (branch) whose orders and settings this installation manages. Every order belongs to
/// exactly one shop through <see cref="Order.ShopId"/>; the whole app works against a single
/// active shop at a time.
/// </summary>
public class Shop
{
    public int Id { get; set; }

    /// <summary>
    /// Stable identity that survives a database import. <see cref="Id"/> is a local autoincrement
    /// value, so two installations will happily allocate the same one — and the database can be
    /// carried between machines wholesale (see <c>GlobalSettingsPackage</c> and
    /// <c>DatabasePathProvider.ImportDatabaseFrom</c>). Anything stored OUTSIDE the database that
    /// belongs to a shop — its measurement terms file, its branding folder — must be keyed on this,
    /// never on <see cref="Id"/>, or an import silently hands one shop another shop's settings.
    /// </summary>
    public Guid PublicId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Short slug for the shop (e.g. "SH"), reserved for disambiguating order numbers between
    /// branches. Order numbers are currently timestamp-based and are only unique per shop by
    /// luck; this is the hook for fixing that without another schema change.
    /// </summary>
    public string? Code { get; set; }

    /// <summary>
    /// Language code to display name, serialized. The shop name is user-facing text and this app
    /// is bilingual, so a single string would force one language's users to read the other's name
    /// on screen and on printed receipts. Mirrors the per-language <c>Names</c> dictionary already
    /// used by <see cref="MeasurementTerm"/> and <see cref="GarmentType"/>.
    /// </summary>
    public string NamesJson { get; set; } = "{}";

    /// <summary>
    /// Language code to street address, serialized — the same shape as <see cref="NamesJson"/> and
    /// for the same reason. An address is prose, not a code: the same building written for a local
    /// reader and written as "101 Tiyu West Rd, Tianhe, Guangzhou" are two different strings, and a
    /// receipt printed in one language should not carry the other's wording.
    ///
    /// Contrast <see cref="PhoneNumber"/> / <see cref="Email"/> / <see cref="Website"/> below,
    /// which are deliberately NOT per language: they are identifiers, identical whoever reads them,
    /// exactly like <c>ReceiptBrandingSettings.TaxRegistrationNumber</c>. Only their labels
    /// translate.
    ///
    /// Nullable, unlike <see cref="NamesJson"/>, and following <see cref="PaymentTaxRulesJson"/>:
    /// null means "never filled in", which is a perfectly ordinary state for an optional field.
    /// It also keeps the migration honest — SQLite's ALTER TABLE ADD COLUMN demands a default for a
    /// NOT NULL column, and a literal '{}' cannot be written in this codebase's DDL (see the note
    /// in EnsureShopSchemaAsync: the raw SQL is treated as a composite format string).
    /// </summary>
    public string? AddressesJson { get; set; }

    /// <summary>Shop contact number. One string for every language; see <see cref="AddressesJson"/>.</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>Shop contact email. One string for every language; see <see cref="AddressesJson"/>.</summary>
    public string? Email { get; set; }

    /// <summary>Shop website. One string for every language; see <see cref="AddressesJson"/>.</summary>
    public string? Website { get; set; }

    /// <summary>
    /// The shop's tax registration number (GST/HST in Canada), printed on its receipts so they
    /// double as tax slips.
    /// </summary>
    /// <remarks>
    /// Lives on the SHOP because it identifies the business, not the document design — a branch has
    /// one registration number whatever its receipts look like. <c>ReceiptBrandingSettings</c> also
    /// carries one, and that one WINS where both are set: the header/footer editor is the more
    /// specific, per-language surface, so a number typed there is a deliberate override of the
    /// shop's. See <c>MainWindow.ResolveTaxRegistrationNumber</c>.
    ///
    /// Not per language, for the same reason as <see cref="PhoneNumber"/>: a registration number is
    /// the same string whoever reads it.
    /// </remarks>
    public string? TaxRegistrationNumber { get; set; }

    /// <summary>Language applied when this shop is opened. Null falls back to the global preference.</summary>
    public string? PreferredLanguageCode { get; set; }

    /// <summary>
    /// Where this shop is, as a tax-jurisdiction code (e.g. "CA", "CN", "JP"). It decides the
    /// standard tax rate the shop seeds its payment rules from and whether its prices are quoted
    /// tax-inclusive — see <c>TaxJurisdictions</c>.
    /// </summary>
    /// <remarks>
    /// Tax is a function of LOCATION, not of language or of how a customer pays, so it gets its own
    /// field rather than being inferred from the installed languages. Null means "never located",
    /// which <c>TaxJurisdictions.For</c> reads back as the home market — exactly how the app behaved
    /// before a shop could say where it is, so no existing branch changes until one is set. A code
    /// whose preset has since been removed from the shipped file resolves to the COUNTRY it names if
    /// that is still shipped ("CA-ON" → "CA", which is what every Canadian shop stored before the
    /// provinces were collapsed into one entry holds), and to the home market otherwise — never a
    /// throw. No migration rewrites the stored code behind the shop's back, so a region that is
    /// shipped again takes effect by itself; saving Shop Settings does persist whatever the picker
    /// shows, which for an unshipped region is the country it resolved to.
    /// </remarks>
    public string? LocationCode { get; set; }

    /// <summary>
    /// When this shop was delisted. Purely a record of WHEN — <see cref="IsArchived"/> is what decides
    /// whether it is in service, and null here is expected on any shop delisted before this column
    /// existed.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT a second flag. <see cref="IsArchived"/> already means "hidden from the picker
    /// without deleting its orders" and is already honoured by the startup shop load, the picker and the
    /// shop-name uniqueness check; adding a parallel <c>DelistedOnUtc is not null</c> test would create
    /// two answers to one question and no rule about which wins. So the bool stays authoritative and
    /// this is the audit stamp beside it, in the spirit of <c>ShopMembership.DeactivatedOn</c>: "closed"
    /// and "closed in March" are different answers and only the second survives being asked next year.
    ///
    /// What Store Management added was not the concept — it was the UI. `IsArchived` had shipped with no
    /// way to set it, which is the "a column nothing ever writes is a landmine" case this project has
    /// hit before.
    /// </remarks>
    public DateTime? DelistedOnUtc { get; set; }

    /// <summary>
    /// Whether this shop is out of service. Delegates to <see cref="IsArchived"/> so there is exactly
    /// one authority; the timestamp beside it is a record, not a second opinion.
    /// </summary>
    [NotMapped]
    public bool IsDelisted => IsArchived;

    /// <summary>
    /// The language codes this shop has installed, as a JSON array — the set its managers and staff
    /// may switch between. A branch serving a bilingual neighbourhood installs two; one that does
    /// not installs one and its people never see a language toggle at all.
    /// </summary>
    /// <remarks>
    /// Null means "never configured", which reads back through
    /// <see cref="InstalledLanguageCodes"/> as just <see cref="PreferredLanguageCode"/> — exactly
    /// how the app behaved before a shop could install more than one language, so no existing
    /// branch changes until somebody installs a second.
    ///
    /// Codes rather than a count or a flag set: a language is identified by the <c>code</c>
    /// attribute inside its <c>*.lang.xml</c>, and languages are DISCOVERED rather than registered,
    /// so there is no enum to point at. A stored code whose file has since been removed is simply
    /// dropped when the set is resolved.
    /// </remarks>
    public string? InstalledLanguagesJson { get; set; }

    /// <summary>
    /// The currency this shop prices new orders in by default. Still a single value after the shop
    /// gained a SET of currencies, because "which one does a new order start in" and "which ones may
    /// it be changed to" are different questions — the same split as
    /// <see cref="PreferredLanguageCode"/> against <see cref="InstalledLanguagesJson"/>.
    /// </summary>
    public CurrencyType CurrencyType { get; set; } = CurrencyType.CAD;

    /// <summary>
    /// The currencies this shop accepts, as a JSON array of <see cref="CurrencyType"/> names — the
    /// set an order may be priced in. A shop on a border, or one taking tourist trade, accepts more
    /// than one; most accept exactly one and their staff never see a currency picker.
    /// </summary>
    /// <remarks>
    /// Null means "never configured", which reads back through <see cref="SupportedCurrencies"/> as
    /// just <see cref="CurrencyType"/> — precisely how the app behaved before a shop could accept
    /// more than one, so no existing branch changes until somebody adds a second.
    ///
    /// Stored as NAMES, not the underlying integers. The numbers are an implementation detail of the
    /// enum and a reordering would silently re-denominate every shop; a name that no longer resolves
    /// is dropped when the set is read, which is the same rule the language codes follow.
    /// </remarks>
    public string? SupportedCurrenciesJson { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Hidden from the shop picker without deleting its orders.</summary>
    public bool IsArchived { get; set; }

    /// <summary>
    /// This shop's tax rule per payment method, serialized. Null means "never configured", which
    /// reads back as <see cref="PaymentTaxRules.CreateDefault"/> — cash and e-transfer tax free,
    /// both card types at 13% — so an existing shop keeps behaving exactly as it always did.
    /// </summary>
    public string? PaymentTaxRulesJson { get; set; }

    /// <summary>How this shop numbers its orders / receipts.</summary>
    public OrderNumberMode OrderNumberMode { get; set; } = OrderNumberMode.Timestamp;

    /// <summary>Leading text of an order number, e.g. "ORD" in ORD-000123. Blank means no prefix.</summary>
    public string? OrderNumberPrefix { get; set; }

    /// <summary>Digits the running number is padded to, so 12 prints as 0012 at a padding of 4.</summary>
    public int OrderNumberPadding { get; set; } = 4;

    /// <summary>
    /// The running number the next order will take. Advanced only after an order is actually
    /// saved, so abandoning a half-filled form does not burn a receipt number.
    /// </summary>
    public int OrderNumberNextSequence { get; set; } = 1;

    /// <summary>
    /// The period <see cref="OrderNumberNextSequence"/> belongs to (a date or a year, depending on
    /// the mode). When the current period no longer matches this, the counter restarts at 1 —
    /// which is what makes daily and yearly numbering reset without a scheduled job.
    /// </summary>
    public string? OrderNumberSequenceKey { get; set; }

    /// <summary>Decoded <see cref="PaymentTaxRulesJson"/>. Computed, never stored directly.</summary>
    [NotMapped]
    public PaymentTaxRules PaymentTaxRules => PaymentTaxRules.FromJson(PaymentTaxRulesJson);

    public void SetPaymentTaxRules(PaymentTaxRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        PaymentTaxRulesJson = rules.ToJson();
    }

    /// <summary>Language code to display name, decoded from <see cref="NamesJson"/>.</summary>
    [NotMapped]
    public Dictionary<string, string> Names => DecodeLocalized(NamesJson);

    /// <summary>Language code to street address, decoded from <see cref="AddressesJson"/>.</summary>
    [NotMapped]
    public Dictionary<string, string> Addresses => DecodeLocalized(AddressesJson);

    /// <summary>
    /// Language codes stored on this shop, exactly as saved. May be empty, which means the shop has
    /// never been told which languages it runs in — <c>ShopLanguages</c> owns what to do about that,
    /// because deciding it here would need the list of languages that actually ship.
    /// </summary>
    [NotMapped]
    public IReadOnlyList<string> InstalledLanguageCodes => DecodeLanguages(InstalledLanguagesJson);

    public void SetNames(IReadOnlyDictionary<string, string> names)
        => NamesJson = JsonSerializer.Serialize(names);

    /// <summary>
    /// Records the languages this shop installs. Blanks and duplicates are dropped so the stored
    /// array says what it means; the caller is responsible for there being at least one, since
    /// "which languages does this branch run in" is a question only a person can answer.
    /// </summary>
    public void SetInstalledLanguages(IEnumerable<string> languageCodes)
    {
        ArgumentNullException.ThrowIfNull(languageCodes);

        var codes = languageCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        InstalledLanguagesJson = codes.Count == 0 ? null : JsonSerializer.Serialize(codes);
    }

    /// <summary>
    /// Currencies stored on this shop, exactly as saved and in stored order. May be empty, meaning
    /// the shop has never been told which it accepts — <c>ShopCurrencies</c> owns what to do about
    /// that, for the same reason <c>ShopLanguages</c> owns the language equivalent: the answer needs
    /// to know what the build actually offers, which the model does not.
    /// </summary>
    [NotMapped]
    public IReadOnlyList<CurrencyType> SupportedCurrencies => DecodeCurrencies(SupportedCurrenciesJson);

    /// <summary>
    /// Records the currencies this shop accepts. Duplicates are dropped so the stored array says
    /// what it means; the caller is responsible for there being at least one, since "what money does
    /// this branch take" is a question only a person can answer.
    /// </summary>
    public void SetSupportedCurrencies(IEnumerable<CurrencyType> currencies)
    {
        ArgumentNullException.ThrowIfNull(currencies);

        var names = currencies.Distinct().Select(currency => currency.ToString()).ToList();
        SupportedCurrenciesJson = names.Count == 0 ? null : JsonSerializer.Serialize(names);
    }

    public void SetAddresses(IReadOnlyDictionary<string, string> addresses)
        => AddressesJson = JsonSerializer.Serialize(addresses);

    /// <summary>
    /// Display name in the requested language, falling back to any other language that has one and
    /// finally to an empty string, so a shop is never nameless on screen.
    /// </summary>
    public string ResolveName(string languageCode) => Resolve(Names, languageCode);

    /// <summary>
    /// Street address in the requested language, with the same fallback as <see cref="ResolveName"/>.
    /// Unlike the name, an empty result is entirely normal — the address is optional, and callers
    /// are expected to omit the line rather than print a blank one.
    /// </summary>
    public string ResolveAddress(string languageCode) => Resolve(Addresses, languageCode);

    /// <summary>
    /// Reads one of the per-language JSON columns. Bad JSON reads back as "nothing set" rather than
    /// throwing: this runs on every display of every shop, and a corrupt column should cost the
    /// shop its address, not stop the picker from opening.
    /// </summary>
    private static Dictionary<string, string> DecodeLocalized(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>();

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// Reads the installed-language array. Bad JSON reads back as "nothing set" for the same reason
    /// <see cref="DecodeLocalized"/> does: a corrupt column should cost the shop its language list,
    /// not stop the shop picker from opening.
    /// </summary>
    private static IReadOnlyList<string> DecodeLanguages(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<string>();

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? (IReadOnlyList<string>)Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Reads the supported-currency array, dropping any name that is no longer a defined
    /// <see cref="CurrencyType"/>. Bad JSON reads back as "nothing set" for the same reason
    /// <see cref="DecodeLocalized"/> does.
    /// </summary>
    /// <remarks>
    /// An unrecognised name is skipped rather than defaulting to anything: guessing here would let a
    /// shop quietly start accepting a currency it never chose, and every guess about money is a
    /// wrong amount on somebody's receipt.
    /// </remarks>
    private static IReadOnlyList<CurrencyType> DecodeCurrencies(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<CurrencyType>();

        List<string>? names;
        try
        {
            names = JsonSerializer.Deserialize<List<string>>(json);
        }
        catch (JsonException)
        {
            return Array.Empty<CurrencyType>();
        }

        if (names is null)
            return Array.Empty<CurrencyType>();

        return names
            .Select(ParseCurrency)
            .Where(currency => currency.HasValue)
            .Select(currency => currency!.Value)
            .Distinct()
            .ToList();
    }

    /// <summary>A stored currency name, or null when it no longer names one.</summary>
    private static CurrencyType? ParseCurrency(string name)
        => Enum.TryParse<CurrencyType>(name, ignoreCase: true, out var currency) && Enum.IsDefined(currency)
            ? currency
            : null;

    private static string Resolve(Dictionary<string, string> values, string languageCode)
    {
        if (values.TryGetValue(languageCode, out var exact) && !string.IsNullOrWhiteSpace(exact))
            return exact;

        return values.Values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }
}

/// <summary>
/// How a shop composes its order / receipt numbers. <see cref="Timestamp"/> is the format the app
/// has always produced and stays the default, so no existing shop's numbering changes until it
/// picks another one.
/// </summary>
public enum OrderNumberMode
{
    /// <summary>PREFIX-20260727-153012 — unique by the second, no counter to keep.</summary>
    Timestamp = 0,

    /// <summary>PREFIX-000123 — one continuous run of numbers.</summary>
    Sequential = 1,

    /// <summary>PREFIX-20260727-0001 — a fresh run of numbers each day.</summary>
    DailySequential = 2,

    /// <summary>PREFIX-2026-0001 — a fresh run of numbers each year.</summary>
    YearlySequential = 3
}
