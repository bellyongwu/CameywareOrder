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
    public PhoneCountry(string code, string dialCode, IReadOnlyList<int> nationalDigits)
    {
        Code = code;
        DialCode = dialCode;
        NationalDigits = nationalDigits;
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

    /// <summary>Localized country name, from the language file's <c>Country.&lt;code&gt;</c> key.</summary>
    public string DisplayName(LocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(localization);
        return localization[$"Country.{Code}"];
    }

    /// <summary>What the picker shows: the dial code, with the country named beside it.</summary>
    public string PickerText(LocalizationService localization) => $"{DialCode}  {DisplayName(localization)}";

    /// <summary>Whether a national number of this many digits is a possible number here.</summary>
    public bool AcceptsDigitCount(int digits) => NationalDigits.Contains(digits);

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
