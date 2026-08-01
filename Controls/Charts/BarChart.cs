using System.Windows;
using System.Windows.Media;

namespace CameywareOrder.Controls.Charts;

/// <summary>
/// A column chart: one labelled bar per <see cref="ChartSlice"/>, with its value written above it.
/// </summary>
/// <remarks>
/// <b>Drawn, not templated.</b> It renders itself in <c>OnRender</c> rather than composing a
/// <c>ItemsControl</c> of rectangles, which is what makes it self-contained (no styles to merge, no
/// converters to register), fast for the handful of bars a report shows, and — the reason that
/// matters here — trivially reusable in the PDF: a <c>RenderTargetBitmap</c> of this element is the
/// same chart the screen shows, so the report and its printout cannot disagree.
///
/// It takes a plain <c>IReadOnlyList</c> rather than binding to an ItemsSource. A chart is redrawn
/// wholesale when its data changes; incremental item tracking would buy nothing and cost a
/// collection-changed subscription to leak.
/// </remarks>
public sealed class BarChart : FrameworkElement
{
    private const double LabelHeight = 18;
    private const double ValueHeight = 18;
    private const double BarGap = 10;
    private const double MinBarWidth = 8;

    public static readonly DependencyProperty SlicesProperty = DependencyProperty.Register(
        nameof(Slices),
        typeof(IReadOnlyList<ChartSlice>),
        typeof(BarChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The bars, in the order they should read.</summary>
    public IReadOnlyList<ChartSlice>? Slices
    {
        get => (IReadOnlyList<ChartSlice>?)GetValue(SlicesProperty);
        set => SetValue(SlicesProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        base.OnRender(drawingContext);

        var slices = Slices;
        if (slices is null || slices.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0)
            return;

        var plotHeight = ActualHeight - LabelHeight - ValueHeight;
        if (plotHeight <= 0)
            return;

        // Against the LARGEST bar, not against the total: a column chart compares magnitudes, and
        // scaling to the sum would flatten every bar the moment a series gained an entry.
        var peak = slices.Max(slice => Math.Max(0, slice.Value));
        var slotWidth = ActualWidth / slices.Count;
        var barWidth = Math.Max(MinBarWidth, slotWidth - BarGap);

        for (var i = 0; i < slices.Count; i++)
        {
            var slice = slices[i];
            var value = Math.Max(0, slice.Value);

            // A zero-valued bar keeps a sliver of height so the category still reads as present
            // rather than as missing from the chart.
            var floor = value > 0 ? 2d : 0d;
            var height = peak <= 0 ? 0 : Math.Max(floor, plotHeight * (value / peak));
            var left = (i * slotWidth) + ((slotWidth - barWidth) / 2);
            var top = ValueHeight + (plotHeight - height);

            drawingContext.DrawRoundedRectangle(
                slice.Fill, null, new Rect(left, top, barWidth, height), 4, 4);

            var valueText = ChartText.Fit(slice.Text, 11, ChartText.Strong, this, slotWidth, bold: true);
            drawingContext.DrawText(
                valueText,
                new Point(left + ((barWidth - valueText.Width) / 2), top - ValueHeight + 2));

            var labelText = ChartText.Fit(slice.Label, 11, ChartText.Muted, this, slotWidth);
            drawingContext.DrawText(
                labelText,
                new Point(left + ((barWidth - labelText.Width) / 2), ActualHeight - LabelHeight + 2));
        }
    }
}
