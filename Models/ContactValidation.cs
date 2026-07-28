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

    // Deliberately loose. Full RFC 5322 rejects addresses that work and accepts ones that do not;
    // what is worth catching here is a typo like a missing @ or domain.
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
}
