using CameywareOrder.Localization;
using CameywareOrder.Models;

namespace CameywareOrder.Services;

/// <summary>
/// The one answer to "which currencies may an order in this shop be priced in". A shop accepts one
/// or more; most accept exactly one and their staff never see a currency picker.
/// </summary>
/// <remarks>
/// Deliberately shaped like <see cref="ShopLanguages"/>, because a reader who has learned one should
/// not have to learn the other. It is simpler in one respect and stricter in another:
///
/// <para><b>Simpler:</b> there is no per-user capability. An administrator sees every LANGUAGE
/// because they work across branches and the language is only how the screen reads. Currency is not
/// a view of an order, it is a fact about it — letting an administrator price an order in a currency
/// the branch does not accept would put a real, wrong number on a real receipt.</para>
///
/// <para><b>Stricter:</b> the answer is bounded by an enum rather than by a discovered folder, so
/// "every currency" is a closed set and a stored value that is not in it is dropped rather than
/// guessed at.</para>
/// </remarks>
public static class ShopCurrencies
{
    /// <summary>The string-table key by which a language declares the currencies of its market.</summary>
    private const string CurrencyCodesKey = "Currency.Codes";

    /// <summary>Every currency this BUILD can name — the bound, not the offer.</summary>
    /// <remarks>
    /// Rarely what a screen wants: use <see cref="Offered(LocalizationService)"/>, which is the set
    /// the installed languages actually imply. This exists for the harness and for the one case that
    /// genuinely means "anything storable".
    /// </remarks>
    public static IReadOnlyList<CurrencyType> All { get; } =
        Enum.GetValues<CurrencyType>().Distinct().ToArray();

    /// <summary>
    /// The currencies a shop may be offered, derived from the languages INSTALLED on the system.
    /// </summary>
    /// <remarks>
    /// Each <c>*.lang.xml</c> declares its own market's currencies under <c>Currency.Codes</c>, so a
    /// new language brings its currency with it and this method never needs editing — the same
    /// promise the language table itself makes. zh-CN declares CNY, ja-JP declares JPY, fr-FR and
    /// es-ES declare EUR.
    ///
    /// <para><b>English leads, and declares two.</b> en-US declares <c>CAD,USD</c> — the application's
    /// home market is Canadian, and a shop there quotes in both. That is data in the file rather than
    /// a branch here, so a build that ships en-CA instead, or neither, needs no code change. The
    /// ORDER is load-bearing: English's currencies come first, and within English CAD precedes USD,
    /// because that is the order a till here would list them in.</para>
    ///
    /// A declared code that is not a <see cref="CurrencyType"/> is dropped rather than guessed at.
    /// The build can only store what the enum names, and inventing a currency would put an amount on
    /// a receipt in money the system cannot actually record.
    /// </remarks>
    public static IReadOnlyList<CurrencyType> Offered(LocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(localization);

        // English first, then the rest in the order the languages themselves are listed. Indexed via
        // Select rather than IndexOf because OrderBy is not documented as stable for a sequence it
        // did not materialise, and "CAD before USD" depends on the order surviving the sort.
        var offered = localization.AvailableLanguages
            .Select((language, index) => (language, index))
            .OrderByDescending(entry => IsEnglish(entry.language))
            .ThenBy(entry => entry.index)
            .Select(entry => entry.language)
            .SelectMany(language => DeclaredCurrencies(language.Code, localization))
            .Distinct()
            .ToList();

        // A table with nothing usable in it would leave a shop unable to price anything at all, so
        // the enum is the floor. Only reachable if every language's declaration is missing or junk.
        return offered.Count > 0 ? offered : All;
    }

    /// <summary>The currencies one language declares, in its declared order.</summary>
    private static IReadOnlyList<CurrencyType> DeclaredCurrencies(string languageCode, LocalizationService localization)
    {
        var declared = localization.GetText(CurrencyCodesKey, languageCode);

        // An unresolved key comes back as the key itself, which is not a currency list.
        if (string.IsNullOrWhiteSpace(declared) || string.Equals(declared, CurrencyCodesKey, StringComparison.Ordinal))
            return Array.Empty<CurrencyType>();

        return declared
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(code => Enum.TryParse<CurrencyType>(code, ignoreCase: true, out var currency)
                            && Enum.IsDefined(currency)
                ? currency
                : (CurrencyType?)null)
            .Where(currency => currency.HasValue)
            .Select(currency => currency!.Value)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Whether a language is an English one — <c>en-US</c>, <c>en-CA</c> or any other en-*. Matched
    /// on the PRIMARY SUBTAG so the rule is about the language, not about which region happens to
    /// ship: the request was "if en-US or en-CA is available, any of them is ok".
    /// </summary>
    private static bool IsEnglish(LanguageOption language)
        => language.Code.Split('-')[0].Equals("en", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The currencies a single language contributes — what the localization panel lists beside it.
    /// </summary>
    public static IReadOnlyList<CurrencyType> ForLanguage(string languageCode, LocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(localization);
        return DeclaredCurrencies(languageCode, localization);
    }

    /// <summary>
    /// The currencies <paramref name="shop"/> accepts. Never empty: a shop that has recorded none
    /// falls back to its own <see cref="Shop.CurrencyType"/>, which is precisely how the application
    /// behaved before a shop could accept more than one — so an existing branch sees no change until
    /// somebody adds a second currency to it.
    /// </summary>
    /// <remarks>
    /// Returned in enum order rather than stored order, so two shops accepting the same pair present
    /// it identically and a re-tick in the editor cannot silently reorder anybody's picker.
    ///
    /// With no shop open nothing is restricted and the whole set is returned — the state the login
    /// and shop-picker screens run in, where no order can be priced anyway.
    /// </remarks>
    public static IReadOnlyList<CurrencyType> Supported(Shop? shop)
        => Supported(shop, LocalizationService.Instance);

    /// <summary>
    /// <see cref="Supported(Shop?)"/> against a named language table rather than the loaded one.
    /// </summary>
    public static IReadOnlyList<CurrencyType> Supported(Shop? shop, LocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(localization);

        var offered = Offered(localization);
        if (shop is null)
            return offered;

        var wanted = shop.SupportedCurrencies;
        if (wanted.Count > 0)
        {
            // Ordered by the OFFER, so every shop presents the same currencies in the same order —
            // English's first, CAD before USD — regardless of what order they were ticked in.
            //
            // A currency the shop accepts but the offer no longer contains (its language was
            // uninstalled) is kept, at the end. Dropping it would silently stop a branch taking
            // money it had explicitly said it takes, and would strand any order already priced in it.
            var known = offered.Where(wanted.Contains);
            var orphaned = wanted.Where(currency => !offered.Contains(currency));
            return known.Concat(orphaned).ToList();
        }

        // Nothing recorded. The shop's own currency is the only thing it has said on the subject,
        // and taking it literally reproduces the behaviour it had before the set existed.
        return Enum.IsDefined(shop.CurrencyType) ? new[] { shop.CurrencyType } : offered;
    }

    /// <summary>
    /// Whether a currency picker is worth showing at all. One currency is not a choice, and a picker
    /// holding a single option is chrome that cannot do anything — the same rule the language toggle
    /// follows.
    /// </summary>
    public static bool CanChoose(Shop? shop) => Supported(shop).Count > 1;

    /// <summary>Whether <paramref name="shop"/> accepts <paramref name="currency"/>.</summary>
    public static bool Offers(Shop? shop, CurrencyType currency) => Supported(shop).Contains(currency);

    /// <summary>
    /// The currency a new order starts in: the shop's preferred one when it accepts it, otherwise
    /// the first currency it does accept.
    /// </summary>
    /// <remarks>
    /// The fallback matters because the two fields can disagree — a shop configured before supported
    /// sets existed, or one whose preferred currency was later un-ticked. Starting an order in a
    /// currency the branch does not take would mean either a picker that cannot return to its own
    /// starting point, or a saved order in money the shop does not handle.
    /// </remarks>
    public static CurrencyType Preferred(Shop? shop)
    {
        var supported = Supported(shop);
        return shop is not null && supported.Contains(shop.CurrencyType) ? shop.CurrencyType : supported[0];
    }

    /// <summary>
    /// The currency an EXISTING order is denominated in, which is a different question from what the
    /// shop currently accepts and must never be answered from the shop.
    /// </summary>
    /// <remarks>
    /// This is the whole point of the per-order currency. An order priced at ￥1,695 stays ￥1,695
    /// after the branch starts taking dollars; reading the shop here would reprint it as "$1,695.00",
    /// which is not a display bug but a wrong amount on a document a customer keeps.
    ///
    /// A value outside the enum — a corrupt or downgraded row — is returned as-is so
    /// <see cref="CurrencySettingService.GetSymbol"/> renders it as unknown rather than as dollars.
    /// </remarks>
    public static CurrencyType Of(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return order.CurrencyType;
    }

    /// <summary>Display symbol for the currency an existing order is denominated in.</summary>
    public static string SymbolOf(Order order) => CurrencySettingService.GetSymbol(Of(order));

    /// <summary>
    /// Plain statement of what a shop takes — "Accepted currencies: CAD, USD" — for the shop editor
    /// and the cards that summarise a branch.
    /// </summary>
    /// <remarks>
    /// Singular and plural are separate keys rather than one string with the count interpolated, for
    /// the same reason the language equivalent splits them: English needs "currency is" against
    /// "currencies are", and it inflects irregularly at that.
    /// </remarks>
    public static string SupportedSummary(Shop? shop, LocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(localization);

        var supported = Supported(shop);
        var names = localization.JoinList(supported.Select(currency => Name(currency, localization)));

        return localization.Format(
            supported.Count == 1 ? "Currency.Supported.One" : "Currency.Supported.Many", names);
    }

    /// <summary>The localized name of a currency, e.g. the "CAD" a receipt prints.</summary>
    public static string Name(CurrencyType currency, LocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(localization);
        return localization[$"CurrencyType.{currency}"];
    }
}
