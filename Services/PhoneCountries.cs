using System.IO;
using System.Text.Json;
using CameywareOrder.Configuration;
using CameywareOrder.Models;

namespace CameywareOrder.Services;

/// <summary>
/// The one answer to "which countries can a phone number be from, what is each one's dial code, and
/// how many digits does a number there have". Loads the shipped rules from
/// <c>Settings/System/Defaults/phone-countries.json</c> once and caches them.
/// </summary>
/// <remarks>
/// Shaped after <see cref="TaxJurisdictions"/> on purpose — a bounded shipped set, read defensively,
/// with a hard fallback so a missing or corrupt file can never leave a form unable to accept a phone
/// number. Adding a country is a line of JSON plus a flag in <c>Themes/Flags.xaml</c> and a
/// <c>Country.&lt;code&gt;</c> name in each language file; no code changes.
/// </remarks>
public static class PhoneCountries
{
    /// <summary>Home market, used when a shop has no location and the file declares nothing.</summary>
    public const string DefaultCode = "CA";

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private static IReadOnlyList<PhoneCountry>? _cached;
    private static string _defaultCode = DefaultCode;

    /// <summary>Every country the build ships, in file order — which is the order the picker lists.</summary>
    public static IReadOnlyList<PhoneCountry> All => _cached ??= Load();

    /// <summary>The country a picker opens on when nothing else decides: the file's declared default.</summary>
    /// <remarks>
    /// Forces <see cref="All"/> before reading the cached code, for the same reason
    /// <see cref="TaxJurisdictions.Default"/> does: only <see cref="Load"/> resolves the field to what
    /// the file declares, so reading it first would answer differently on the very first call.
    /// </remarks>
    public static PhoneCountry Default
    {
        get
        {
            var all = All;
            return Find(_defaultCode) ?? all[0];
        }
    }

    /// <summary>The country for a code, or null when it is blank or not shipped.</summary>
    public static PhoneCountry? Find(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        return All.FirstOrDefault(c => string.Equals(c.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The country a shop's phone fields open on: its LOCATION, else the location its currency implies,
    /// else the home market.
    /// </summary>
    /// <remarks>
    /// Location first because it is the precise answer and the currency is only ever an inference — a
    /// shop in Barcelona trading in EUR maps to France through <c>LocationForCurrency</c>, so defaulting
    /// off the currency would open every Spanish number on +33. The currency step still earns its place:
    /// it is what a shop that predates the location setting has already said about where it operates.
    ///
    /// A location whose country ships no phone rule (or a regional code like "CA-ON") resolves through
    /// its COUNTRY part, the same widening <c>TaxJurisdictions.For</c> does.
    /// </remarks>
    public static PhoneCountry ForShop(Shop? shop)
    {
        if (shop is null)
            return Default;

        return FindByLocation(shop.LocationCode)
            ?? FindByLocation(TaxJurisdictions.LocationForCurrency(shop.CurrencyType))
            ?? Default;
    }

    /// <summary>A location code as a phone country, widening "CA-ON" to "CA".</summary>
    private static PhoneCountry? FindByLocation(string? locationCode)
    {
        var exact = Find(locationCode);
        if (exact is not null)
            return exact;

        if (string.IsNullOrWhiteSpace(locationCode))
            return null;

        var trimmed = locationCode.Trim();
        var separator = trimmed.IndexOf('-');

        return separator > 0 ? Find(trimmed[..separator]) : null;
    }

    /// <summary>
    /// Splits a stored number into the country it names and the national part — "+86 138 0013 8000"
    /// becomes China and "138 0013 8000".
    /// </summary>
    /// <remarks>
    /// Matched LONGEST DIAL CODE FIRST, so "+1" never swallows a "+123". Anything that does not begin
    /// with a recognised code — every number stored before this feature existed — comes back as
    /// (<paramref name="fallback"/>, the original text) rather than being rewritten: a number already
    /// in the database is a fact about a customer, not something to reformat on read.
    ///
    /// Two countries can share a dial code (Canada and the US are both +1). The first shipped match
    /// wins, which is why the file lists Canada first — the home market is the likelier reading, and
    /// nothing about the number itself can distinguish them.
    /// </remarks>
    public static (PhoneCountry Country, string National) Split(string? stored, PhoneCountry? fallback = null)
    {
        var value = stored?.Trim() ?? string.Empty;
        var home = fallback ?? Default;

        if (value.Length == 0)
            return (home, string.Empty);

        foreach (var country in All.OrderByDescending(c => c.DialCode.Length))
        {
            if (!value.StartsWith(country.DialCode, StringComparison.Ordinal))
                continue;

            return (country, value[country.DialCode.Length..].Trim());
        }

        return (home, value);
    }

    /// <summary>Joins a country and a national number back into what is stored and printed.</summary>
    public static string Compose(PhoneCountry? country, string? national)
    {
        var number = national?.Trim() ?? string.Empty;
        if (number.Length == 0)
            return string.Empty;

        return country is null ? number : $"{country.DialCode} {number}";
    }

    private static IReadOnlyList<PhoneCountry> Load()
    {
        try
        {
            var path = SystemSettingsPaths.PhoneCountriesFile;
            if (!File.Exists(path))
                return Fallback;

            var payload = JsonSerializer.Deserialize<CountriesPayload>(File.ReadAllText(path), ReadOptions);
            var entries = payload?.Countries;
            if (entries is null || entries.Count == 0)
                return Fallback;

            var parsed = entries
                .Where(e => !string.IsNullOrWhiteSpace(e.Code)
                            && !string.IsNullOrWhiteSpace(e.DialCode)
                            && e.NationalDigits is { Count: > 0 })
                .Select(e => new PhoneCountry(e.Code!.Trim(), e.DialCode!.Trim(),
                    e.NationalDigits!.Where(d => d > 0).ToList(),
                    ParseFormats(e.NationalFormat)))
                .Where(c => c.NationalDigits.Count > 0)
                .ToList();

            if (parsed.Count == 0)
                return Fallback;

            if (!string.IsNullOrWhiteSpace(payload!.DefaultCountryCode)
                && parsed.Any(c => string.Equals(c.Code, payload.DefaultCountryCode, StringComparison.OrdinalIgnoreCase)))
            {
                _defaultCode = payload.DefaultCountryCode!.Trim();
            }

            return parsed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Fallback;
        }
    }

    /// <summary>
    /// Reads the grouping patterns, whose JSON keys are digit counts written as strings. Parsed by hand
    /// rather than deserialized into a <c>Dictionary&lt;int, string&gt;</c> so that one unparsable key
    /// costs that one entry instead of the whole country: a file edited by hand is the point of shipping
    /// it as JSON, and a typo there must not take a dial code down with it.
    /// </summary>
    private static IReadOnlyDictionary<int, string> ParseFormats(Dictionary<string, string>? entries)
    {
        var formats = new Dictionary<int, string>();
        if (entries is null)
            return formats;

        foreach (var (key, pattern) in entries)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                continue;

            if (!int.TryParse(key, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var digits) || digits <= 0)
            {
                continue;
            }

            // A pattern with fewer slots than its key claims would silently drop digits off the end.
            if (pattern.Count(c => c == '#') != digits)
                continue;

            formats[digits] = pattern;
        }

        return formats;
    }

    /// <summary>Built-in home market, used only when the shipped file is missing or unreadable.</summary>
    /// <remarks>
    /// Carries the NANP grouping rather than an empty map: this is the fallback a shop actually runs on
    /// when the file cannot be read, and a build that quietly stops punctuating numbers is the kind of
    /// difference nobody reports as a bug.
    /// </remarks>
    private static IReadOnlyList<PhoneCountry> Fallback { get; } = new[]
    {
        new PhoneCountry(DefaultCode, "+1", new[] { 10 },
            new Dictionary<int, string> { [10] = "###-###-####" })
    };

    private sealed record CountriesPayload(string? DefaultCountryCode, List<CountryEntry>? Countries);

    private sealed record CountryEntry(string? Code, string? DialCode, List<int>? NationalDigits,
        Dictionary<string, string>? NationalFormat);
}
