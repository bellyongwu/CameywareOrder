using System.Globalization;
using System.Text.RegularExpressions;

namespace CameywareOrder.Models;

/// <summary>
/// How a tax RATE is written, read and bounded — the one definition, shared by every screen that
/// shows or accepts one.
/// </summary>
/// <remarks>
/// Rates are not money and do not round like it. Quebec's combined GST+QST is <c>14.975%</c>, and a
/// two-decimal format turns that into <c>14.98</c> — which is not merely a display nicety, because
/// the settings screen seeds its edit box from the formatted string and re-saving then writes the
/// rounded number back. The rate survived storage perfectly (it is a <c>decimal</c> the whole way)
/// and was lost on the way through the screen that edits it.
///
/// Three decimals is the declared limit rather than an arbitrary one: it is what the real published
/// rates need, and it is stated HERE so the input filter, the parser and the display cannot disagree
/// about it. A fourth decimal is refused at the keyboard rather than accepted and quietly rounded —
/// a shop that types a rate it cannot have must be told, not have its number edited on the way past.
/// </remarks>
public static class TaxRateFormat
{
    /// <summary>Decimal places a rate may carry.</summary>
    public const int MaxDecimals = 3;

    /// <summary>The highest rate that can be entered. A rate above 100% is a typo, not a jurisdiction.</summary>
    public const decimal MaxPercent = 100m;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    // Built FROM MaxDecimals rather than repeating the number, so the constant and the rule cannot
    // drift apart — the whole reason this type exists is that the input filter, the parser and the
    // display had each grown their own answer.
    //
    // Partial input, so it must accept what a half-typed rate looks like: "", "14", "14." and "14.9"
    // are all on the way to "14.975". A pattern that only matched a FINISHED rate would refuse the
    // decimal point, and the field could never reach three places at all.
    private static readonly Regex Partial = new(
        $@"^\d*(\.\d{{0,{MaxDecimals}}})?$", RegexOptions.None, RegexTimeout);

    /// <summary>Whether <paramref name="proposed"/> is a rate or the beginning of one.</summary>
    /// <remarks>
    /// For <c>PreviewTextInput</c> and paste, where the text is judged mid-edit. It deliberately does
    /// NOT check the 0..100 range: "1" is the first keystroke of "14.975", and refusing digits by
    /// range would make a two-digit rate untypeable.
    /// </remarks>
    public static bool IsPartialRate(string? proposed)
        => string.IsNullOrEmpty(proposed) || Partial.IsMatch(proposed);

    /// <summary>A finished rate, or false when what was typed is not one.</summary>
    public static bool TryParse(string? text, out decimal ratePercent)
    {
        ratePercent = 0m;

        var value = text?.Trim() ?? string.Empty;
        if (!Partial.IsMatch(value))
            return false;

        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed))
            return false;

        if (parsed < 0m || parsed > MaxPercent)
            return false;

        ratePercent = parsed;
        return true;
    }

    /// <summary>The rate as it is shown and as it is seeded into an edit box: "13", "14.975", "6.5".</summary>
    /// <remarks>
    /// Trailing zeros are dropped, so a whole rate reads "13" rather than "13.000" — the round-trip
    /// through the settings screen has to be lossless, and it stays so because parsing "13" gives
    /// back exactly 13.
    /// </remarks>
    public static string Text(decimal ratePercent)
        => ratePercent.ToString("0.###", CultureInfo.CurrentCulture);

    /// <summary>The rate with its percent sign, for a label: "14.975%".</summary>
    public static string Percent(decimal ratePercent) => $"{Text(ratePercent)}%";
}
