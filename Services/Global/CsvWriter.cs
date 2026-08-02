using System.Globalization;
using System.IO;
using System.Text;

namespace CameywareOrder.Services;

/// <summary>
/// Builds a CSV file — RFC 4180 quoting, and a UTF-8 byte-order mark so Excel opens it correctly.
/// </summary>
/// <remarks>
/// Small, and deliberately not a dependency. The whole of CSV that matters is the quoting rule and
/// the encoding, and both are things a library would hide rather than settle:
///
/// - **The BOM is not optional here.** Excel on Windows reads a BOM-less file as the system ANSI
///   codepage, so every Chinese, Japanese, French and Spanish name in the sheet comes out as mojibake
///   — on the one machine the shop will actually open it on. This application is multilingual by
///   design, so a writer without a BOM would be wrong for most of its users most of the time.
/// - **A value is quoted whenever it contains a comma, a quote, a newline or leading/trailing
///   space.** A tailoring order's notes contain all four.
/// - **A leading <c>=</c>, <c>+</c>, <c>-</c> or <c>@</c> is neutralised.** A spreadsheet treats such
///   a cell as a FORMULA, so a customer name or a note typed as "=cmd" is a CSV-injection vector the
///   moment the file is opened. Prefixed with a tab inside the quotes, which Excel shows as text.
///
/// Reusable by anything that needs a sheet — the order export uses it today; a settlement export
/// would use the same one rather than growing a second quoting rule.
/// </remarks>
public sealed class CsvWriter
{
    private static readonly char[] MustQuote = { ',', '"', '\r', '\n' };

    private readonly StringBuilder _builder = new();
    private readonly CultureInfo _culture;

    /// <param name="culture">
    /// How numbers and dates are rendered. <see cref="CultureInfo.CurrentCulture"/> is what the shop
    /// reading the file expects; pass <see cref="CultureInfo.InvariantCulture"/> for a sheet meant to
    /// be parsed by something else.
    /// </param>
    public CsvWriter(CultureInfo? culture = null) => _culture = culture ?? CultureInfo.CurrentCulture;

    /// <summary>How many rows have been written, header included.</summary>
    public int RowCount { get; private set; }

    /// <summary>Appends one row.</summary>
    public void WriteRow(params object?[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        WriteRow((IReadOnlyList<object?>)values);
    }

    /// <summary>Appends one row.</summary>
    public void WriteRow(IReadOnlyList<object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        for (var index = 0; index < values.Count; index++)
        {
            if (index > 0)
                _builder.Append(',');

            _builder.Append(Escape(Render(values[index])));
        }

        // CRLF, which is what RFC 4180 specifies and what every Windows spreadsheet expects.
        _builder.Append("\r\n");
        RowCount++;
    }

    /// <summary>The file as text, without the byte-order mark. <see cref="Save"/> adds that.</summary>
    public override string ToString() => _builder.ToString();

    /// <summary>Writes the file, with the BOM. See the remarks on the class for why that matters.</summary>
    public void Save(string path)
    {
        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, _builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private string Render(object? value) => value switch
    {
        null => string.Empty,
        string text => text,
        // Rendered to two places rather than left to the default, so a money column lines up and a
        // total typed back into a calculator matches the receipt.
        decimal amount => amount.ToString("0.00", _culture),
        DateTime moment => moment.ToString("yyyy-MM-dd HH:mm", _culture),
        DateOnly day => day.ToString("yyyy-MM-dd", _culture),
        bool flag => flag ? "1" : "0",
        IFormattable formattable => formattable.ToString(null, _culture),
        _ => value.ToString() ?? string.Empty
    };

    private static string Escape(string value)
    {
        if (value.Length == 0)
            return value;

        var neutralised = NeutraliseFormula(value);

        var needsQuotes = neutralised != value
                          || neutralised.IndexOfAny(MustQuote) >= 0
                          || char.IsWhiteSpace(neutralised[0])
                          || char.IsWhiteSpace(neutralised[^1]);

        return needsQuotes ? '"' + neutralised.Replace("\"", "\"\"") + '"' : neutralised;
    }

    /// <summary>Stops a spreadsheet reading a stored value as a formula. See the remarks on the class.</summary>
    private static string NeutraliseFormula(string value)
        => value[0] is '=' or '+' or '-' or '@' ? "\t" + value : value;
}
