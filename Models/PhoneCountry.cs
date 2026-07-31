using CameywareOrder.Localization;

namespace CameywareOrder.Models;

/// <summary>
/// One country's phone rule: the international dial code shown in front of a number, and how many
/// digits a national number there has. Loaded from
/// <c>Settings/System/Defaults/phone-countries.json</c> by <c>PhoneCountries</c>.
/// </summary>
/// <remarks>
/// Deliberately NOT folded into <see cref="TaxJurisdiction"/>, whose codes happen to match today. A
/// tax jurisdiction is a market this build sells INTO; a phone country is anywhere a customer's number
/// can come FROM, and the second list is the longer one the moment a shop serves a visitor. Sharing
/// one table would force a choice between shipping tax presets for countries nobody trades in and
/// refusing numbers from countries customers actually call from.
///
/// The two are related in exactly one place, and only as a DEFAULT: a shop's location code selects the
/// country this control opens on (see <c>PhoneCountries.ForShop</c>). What a given number is validated
/// against is whatever the person entering it picked.
/// </remarks>
public sealed class PhoneCountry
{
    /// <param name="nationalFormats">
    /// Grouping patterns keyed by digit count; empty for a country this build has no rule for.
    /// Required rather than optional ON PURPOSE — an optional parameter would let every existing
    /// call site keep compiling while silently declaring "this country has no format", which is
    /// indistinguishable from a country that genuinely has none.
    /// </param>
    /// <param name="nationalPattern">
    /// Matched against the DIGITS of a national number, or null for a country that ships none — in
    /// which case <paramref name="nationalDigits"/> decides alone.
    /// </param>
    public PhoneCountry(string code, string dialCode, IReadOnlyList<int> nationalDigits,
        IReadOnlyDictionary<int, string> nationalFormats,
        System.Text.RegularExpressions.Regex? nationalPattern)
    {
        Code = code;
        DialCode = dialCode;
        NationalDigits = nationalDigits;
        NationalFormats = nationalFormats;
        NationalPattern = nationalPattern;
    }

    /// <summary>ISO country code — "CA", "CN", "JP". Matches a tax jurisdiction's code where one exists.</summary>
    public string Code { get; }

    /// <summary>International calling code, written with its plus: "+1", "+86".</summary>
    public string DialCode { get; }

    /// <summary>
    /// The digit counts a national number may have here, excluding the dial code. A list rather than a
    /// single number because Japan has both 10- and 11-digit numbers, and a rule that admits only one
    /// of them refuses half the country.
    /// </summary>
    public IReadOnlyList<int> NationalDigits { get; }

    /// <summary>
    /// How a number of a given digit count is grouped — <c>10</c> to <c>"###-###-####"</c>. A count
    /// with no entry, and a country with no entries at all, is not grouped.
    /// </summary>
    public IReadOnlyDictionary<int, string> NationalFormats { get; }

    /// <summary>
    /// Groups a national number the way this country writes it — "9054016667" becomes
    /// "905-401-6667" — and does so PROGRESSIVELY, so it can run on every keystroke.
    /// </summary>
    /// <remarks>
    /// Punctuation is emitted only when a digit still follows it. That is what makes the field usable
    /// while typing: a trailing separator appears the moment the next digit does, never before, so the
    /// caret never sits behind a dash the user did not ask for and backspace never fights the format.
    ///
    /// The pattern is chosen as the SHORTEST declared length that can still hold what has been typed,
    /// because a half-typed number has no length yet — with Japan's 10- and 11-digit forms, four digits
    /// have to be grouped under some assumption, and the shorter one is the one that stays right for
    /// longest. Typing past every declared length returns the text UNCHANGED rather than regrouping it:
    /// at that point the number is not one this country writes, and inventing punctuation for it would
    /// disguise a number the validator is about to refuse.
    /// </remarks>
    public string FormatNational(string? national)
    {
        var raw = national ?? string.Empty;
        if (NationalFormats.Count == 0 || raw.Length == 0)
            return raw;

        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
            return raw;

        // A COMPLETE number of a length this country accepts but has no pattern for is shown as bare
        // digits. Japan is the case: ten digits is a real Japanese number, but 03-1234-5678 and
        // 045-123-4567 are both correct and the digits do not say which, so it must not be borrowed
        // into the eleven-digit mobile grouping merely because that pattern is long enough to hold it.
        // Shorter counts DO borrow it, because a half-typed number is not yet any length at all — and
        // that borrowed punctuation is exactly what has to be taken back off on reaching ten, or a
        // Japanese landline would keep the dashes it collected on the way there.
        if (!NationalFormats.ContainsKey(digits.Length) && AcceptsDigitCount(digits.Length))
            return digits;

        var pattern = PatternFor(digits.Length);
        if (pattern is null)
            return raw;

        var grouped = new System.Text.StringBuilder(pattern.Length);
        var next = 0;

        foreach (var slot in pattern)
        {
            if (next >= digits.Length)
                break;

            grouped.Append(slot == '#' ? digits[next++] : slot);
        }

        return grouped.ToString();
    }

    /// <summary>The grouping to use for a number this many digits long, or null when there is none.</summary>
    private string? PatternFor(int digitCount)
    {
        string? best = null;
        var bestLength = int.MaxValue;

        foreach (var (length, pattern) in NationalFormats)
        {
            if (length < digitCount || length >= bestLength)
                continue;

            best = pattern;
            bestLength = length;
        }

        return best;
    }

    /// <summary>Localized country name, from the language file's <c>Country.&lt;code&gt;</c> key.</summary>
    public string DisplayName(LocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(localization);
        return localization[$"Country.{Code}"];
    }

    /// <summary>What the picker shows: the dial code, with the country named beside it.</summary>
    public string PickerText(LocalizationService localization) => $"{DialCode}  {DisplayName(localization)}";

    /// <summary>
    /// The shape a national number takes here, matched against its digits alone — or null when this
    /// country ships no pattern and length is the only rule.
    /// </summary>
    public System.Text.RegularExpressions.Regex? NationalPattern { get; }

    /// <summary>Whether a national number of this many digits is a possible LENGTH here.</summary>
    /// <remarks>
    /// Length only. <see cref="AcceptsNationalNumber"/> is the actual test — this stays public
    /// because the error message needs to know whether the length was the problem, which is what
    /// decides between "a number here has 10 digits" and "that is not a number here".
    /// </remarks>
    public bool AcceptsDigitCount(int digits) => NationalDigits.Contains(digits);

    /// <summary>
    /// Whether <paramref name="national"/> is a number this country actually issues.
    /// </summary>
    /// <remarks>
    /// The pattern where there is one, the digit count where there is not. Counting digits alone
    /// cannot see an area code beginning 0 or 1, a Chinese mobile that does not start with 1, or a
    /// Japanese number missing its trunk 0 — each has exactly the right number of digits and is still
    /// impossible. Every one of those was accepted before the patterns were added.
    ///
    /// Punctuation is stripped first, so the caller may pass "289-990-3357" or "289 990 3357" and the
    /// pattern only ever describes digits. That keeps each pattern readable and stops six countries
    /// from each having to re-state which separators people type.
    /// </remarks>
    public bool AcceptsNationalNumber(string? national)
    {
        var digits = new string((national ?? string.Empty).Where(char.IsDigit).ToArray());

        if (NationalPattern is null)
            return AcceptsDigitCount(digits.Length);

        // Length is still checked, because a pattern and a declared length can disagree only by
        // mistake and the length list is what the error message quotes.
        return AcceptsDigitCount(digits.Length) && NationalPattern.IsMatch(digits);
    }

    /// <summary>
    /// The lengths this country accepts, as prose for an error message — "10", or "10 or 11". Built
    /// from the DATA rather than written into each language file, so editing the JSON is enough.
    /// </summary>
    public string ExpectedDigitsText(LocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(localization);

        return NationalDigits.Count switch
        {
            0 => string.Empty,
            1 => NationalDigits[0].ToString(System.Globalization.CultureInfo.CurrentCulture),
            _ => string.Join(localization["Format.ListSeparator"],
                NationalDigits.Select(d => d.ToString(System.Globalization.CultureInfo.CurrentCulture)))
        };
    }
}
