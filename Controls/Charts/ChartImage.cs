using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CameywareOrder.Controls.Charts;

/// <summary>
/// Renders a chart to a PNG, so the same control that is on screen goes into the PDF.
/// </summary>
/// <remarks>
/// The alternative — drawing the chart a second time with the PDF library's own primitives — is two
/// implementations of one picture, and they drift the first time a colour or a label rule changes.
/// This way the printed chart IS the screen's chart.
///
/// It measures and arranges the element itself, so it works on one that was never added to a window.
/// WPF only, and therefore only callable from the UI thread — which is why <c>SettlementDocument</c>
/// takes bytes rather than doing this itself and dragging WPF into a layout that is otherwise
/// headless and testable.
/// </remarks>
public static class ChartImage
{
    /// <summary>PNG bytes of <paramref name="element"/> at the given size, or null if it cannot be drawn.</summary>
    /// <param name="scale">
    /// Pixels per DIP. 2 gives a chart that stays sharp when the PDF is printed or zoomed; 1 would
    /// look correct on screen and soft on paper.
    /// </param>
    public static byte[]? Render(FrameworkElement element, double width, double height, double scale = 2)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (width <= 0 || height <= 0)
            return null;

        var size = new Size(width, height);
        element.Measure(size);
        element.Arrange(new Rect(size));
        element.UpdateLayout();

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(width * scale),
            (int)Math.Ceiling(height * scale),
            96 * scale,
            96 * scale,
            PixelFormats.Pbgra32);

        bitmap.Render(element);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
