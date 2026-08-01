using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace CameywareOrder.Controls.Charts;

/// <summary>
/// The type both charts draw with, in one place so they cannot drift apart.
/// </summary>
/// <remarks>
/// A chart drawn with <c>OnRender</c> has no template and therefore no implicit style, so the font
/// does not arrive from the theme the way it does for a <c>TextBlock</c>. Rather than each chart
/// carrying its own <c>Typeface</c> and pixels-per-dip dance, both take it from here.
/// </remarks>
internal static class ChartText
{
    private static readonly Typeface Face = new("Segoe UI");

    internal static readonly Brush Muted = FrozenGrey(0x6B, 0x72, 0x80);
    internal static readonly Brush Strong = FrozenGrey(0x11, 0x18, 0x27);
    internal static readonly Brush OnFill = Brushes.White;

    /// <summary>A run of text ready to draw, measured at the visual's own device scale.</summary>
    internal static FormattedText Make(string text, double size, Brush brush, Visual visual, bool bold = false)
    {
        var typeface = bold
            ? new Typeface(Face.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal)
            : Face;

        return new FormattedText(
            text ?? string.Empty,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            size,
            brush,
            VisualTreeHelper.GetDpi(visual).PixelsPerDip);
    }

    /// <summary>
    /// The same, trimmed with an ellipsis to <paramref name="maxWidth"/>.
    /// </summary>
    /// <remarks>
    /// Charts are the one place a long label cannot simply wrap — a bar is as wide as it is — so
    /// trimming is set here rather than left to each caller to forget.
    /// </remarks>
    internal static FormattedText Fit(string text, double size, Brush brush, Visual visual, double maxWidth, bool bold = false)
    {
        var formatted = Make(text, size, brush, visual, bold);
        formatted.MaxTextWidth = Math.Max(1, maxWidth);
        formatted.MaxLineCount = 1;
        formatted.Trimming = TextTrimming.CharacterEllipsis;
        return formatted;
    }

    private static Brush FrozenGrey(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
