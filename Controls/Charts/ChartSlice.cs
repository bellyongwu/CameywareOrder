using System.Windows.Media;

namespace CameywareOrder.Controls.Charts;

/// <summary>One labelled value in a chart.</summary>
/// <param name="Label">What to write under the bar or beside the wedge.</param>
/// <param name="Value">The magnitude. Negatives are treated as zero by both charts.</param>
/// <param name="Fill">The colour. Take one from <see cref="ChartPalette"/> unless the value has a meaning of its own.</param>
public sealed record ChartSlice(string Label, double Value, Brush Fill)
{
    /// <summary>The value formatted for display — set it when a raw number is not what a reader wants.</summary>
    public string? DisplayValue { get; init; }

    public string Text => DisplayValue ?? Value.ToString("N2");
}

/// <summary>
/// The colours a chart uses when the data has no meaning of its own to encode.
/// </summary>
/// <remarks>
/// One ordered set, taken in order, so two charts of the same data agree — the service line that is
/// indigo in the pie is indigo in the bars. Built from the application's own accent family rather
/// than from a generic rainbow, and kept to hues that stay distinguishable side by side and readable
/// with white text on top. Frozen: these are handed to every chart on every render.
/// </remarks>
public static class ChartPalette
{
    private static readonly Brush[] Series =
    {
        Frozen(0x4F, 0x46, 0xE5),   // indigo — the application's accent
        Frozen(0x0E, 0x94, 0x88),   // teal
        Frozen(0xF5, 0x9E, 0x0B),   // amber
        Frozen(0x8B, 0x5C, 0xF6),   // violet
        Frozen(0xEC, 0x48, 0x99),   // pink
        Frozen(0x22, 0xC5, 0x5E),   // green
        Frozen(0x64, 0x74, 0x8B)    // slate — the quiet one, last
    };

    /// <summary>The nth colour, wrapping round for a series longer than the palette.</summary>
    public static Brush At(int index) => Series[((index % Series.Length) + Series.Length) % Series.Length];

    /// <summary>Money that came in.</summary>
    public static Brush Positive => Frozen(0x22, 0xC5, 0x5E);

    /// <summary>Money still owed.</summary>
    public static Brush Outstanding => Frozen(0xF5, 0x9E, 0x0B);

    /// <summary>Money that went back out.</summary>
    public static Brush Negative => Frozen(0xEF, 0x44, 0x44);

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
