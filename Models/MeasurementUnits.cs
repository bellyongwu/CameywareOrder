using System.Globalization;
using System.Text.RegularExpressions;

namespace CameywareOrder.Models;

/// <summary>
/// Conversion between the two units a measurement can be written in, and the rule for reading a
/// measurement in a unit it was not typed in.
/// </summary>
/// <remarks>
/// Shared rather than repeated. The editor converts when the user flips the cm/inch toggle, and the
/// printed sheet and the exported PDF have to produce the SAME figure — a receipt that disagrees
/// with the screen about a customer's chest measurement is worse than one that shows nothing.
/// </remarks>
public static class MeasurementUnits
{
    public const decimal CentimetersPerInch = 2.54m;

    // Backstop against pathological backtracking on pasted input (S6444).
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// A measurement is digits plus an optional trailing + or -, e.g. "20.5" or "20+". The suffix is
    /// a tailor's note that the figure runs over or under; converting must touch only the digits and
    /// carry the mark through untouched.
    /// </summary>
    private static readonly Regex NumberWithSuffix =
        new(@"^(\d+(?:\.\d*)?)([+-]?)$", RegexOptions.None, RegexTimeout);

    /// <summary>
    /// Converts a measurement between units, preserving any trailing +/-. Anything that is not a
    /// plain number is returned unchanged: a free-text note is not ours to reinterpret.
    /// </summary>
    public static string Convert(string? text, bool toInch)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text ?? string.Empty;

        var trimmed = text.Trim();
        var match = NumberWithSuffix.Match(trimmed);
        if (!match.Success || !decimal.TryParse(match.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            return trimmed;

        var converted = toInch ? value / CentimetersPerInch : value * CentimetersPerInch;
        var rounded = Math.Round(converted, 2, MidpointRounding.AwayFromZero);
        return rounded.ToString("0.##", CultureInfo.InvariantCulture) + match.Groups[2].Value;
    }

    /// <summary>
    /// The figure to show in the requested unit, converting from the other one when the requested
    /// unit was never filled in. Null only when neither unit holds anything.
    /// </summary>
    /// <remarks>
    /// This is the difference between printing a measurement sheet and printing an empty one.
    /// A value carries BOTH units only if the editor's unit toggle happened to be flipped while it
    /// was on screen; measurements typed in cm and saved have no inch figure at all. Of 768 values
    /// stored on this installation, 768 had a cm figure and 39 had an inch one — so a reader that
    /// treats "no inch figure" as "no value" drops 95% of the rows, and every row of an order that
    /// was never toggled, which renders as a sheet with nothing on it.
    /// </remarks>
    public static string? Resolve(string? centimetres, string? inches, bool wantInches)
    {
        var requested = wantInches ? inches : centimetres;
        if (!string.IsNullOrWhiteSpace(requested))
            return requested.Trim();

        var other = wantInches ? centimetres : inches;
        return string.IsNullOrWhiteSpace(other) ? null : Convert(other, toInch: wantInches);
    }
}
