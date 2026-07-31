using System.Text.RegularExpressions;

namespace CameywareOrder.Models;

/// <summary>
/// What counts as a usable phone number or email address.
/// </summary>
/// <remarks>
/// One definition, shared by every screen that collects contact details — the customer on an order
/// and the staff member on the roster. The rules lived privately inside <c>OrderEditWindow</c>, and
/// a second copy for members would have been free to drift: an address the order form rejects but
/// the roster accepts is a bug nobody notices until mail bounces.
///
/// Blank is VALID in both. These are optional fields; "required" is a separate question the caller
/// answers, because the order form demands an email only when a payment method needs one.
/// </remarks>
public static class ContactValidation
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    // Deliberately loose. Full RFC 5322 rejects addresses that work and accepts ones that do not.
    // What is worth catching here is a typo such as a missing @ or a missing domain.
    private static readonly Regex EmailPattern =
        new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.None, RegexTimeout);

    // Digits, and the punctuation people actually type around them. No country assumptions: this
    // application already runs shops in China and Canada.
    private static readonly Regex PhoneShape =
        new(@"^\+?[\d\s\-().]+$", RegexOptions.None, RegexTimeout);

    public static bool IsValidEmail(string? email)
    {
        var value = email?.Trim() ?? string.Empty;
        return value.Length == 0 || EmailPattern.IsMatch(value);
    }

    public static bool IsValidPhone(string? phone)
    {
        var value = phone?.Trim() ?? string.Empty;
        if (value.Length == 0)
            return true;

        if (!PhoneShape.IsMatch(value))
            return false;

        // 7 is the shortest local number still in use; 15 is the E.164 maximum. Counting DIGITS
        // rather than characters is what lets the same rule accept "905-401-6667" and "+86 20 8888 8888".
        var digits = value.Count(char.IsDigit);
        return digits is >= 7 and <= 15;
    }

    /// <summary>
    /// Whether a NATIONAL number — the part after the dial code — is a possible number in
    /// <paramref name="country"/>: the right shape, and one of the digit counts that country uses.
    /// </summary>
    /// <remarks>
    /// The country is the one PICKED FOR THIS NUMBER, never the shop's own. A Toronto shop takes a
    /// visiting customer's Shanghai mobile, and checking that against Canada's ten digits would refuse
    /// a number that is perfectly correct.
    ///
    /// Kept beside <see cref="IsValidPhone(string?)"/> rather than replacing it. The loose rule is what
    /// every record saved before this feature was validated under, and it stays the rule for those:
    /// tightening retroactively would mean an order from last year could no longer be saved at all
    /// until someone re-typed a phone number they have no way to verify. New records get this one.
    ///
    /// A null country means the number names no country the build ships — a legacy number carrying an
    /// unrecognised prefix — so the loose rule answers instead of a rule for a country nobody chose.
    /// </remarks>
    public static bool IsValidNationalPhone(string? national, PhoneCountry? country)
    {
        var value = national?.Trim() ?? string.Empty;
        if (value.Length == 0)
            return true;

        if (!PhoneShape.IsMatch(value))
            return false;

        if (country is null)
            return IsValidPhone(value);

        // The country's own pattern where it ships one, its digit count where it does not. Counting
        // digits alone accepted an area code beginning 0, a Chinese mobile not starting with 1, and a
        // Japanese number without its trunk 0 — all the right length, none of them real.
        return country.AcceptsNationalNumber(value);
    }
}
