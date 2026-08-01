using System.Windows;
using System.Windows.Media;

namespace CameywareOrder.Controls.Charts;

/// <summary>
/// A doughnut chart with a legend beside it: one wedge per <see cref="ChartSlice"/>, sized by share.
/// </summary>
/// <remarks>
/// Drawn in <c>OnRender</c> for the reasons on <see cref="BarChart"/> — self-contained, and the same
/// element renders into the PDF so the printout matches the screen.
///
/// A DOUGHNUT rather than a solid pie: the hole is where the total goes, which is the number a
/// reader wants first and would otherwise need a second caption to find. Set <see cref="CentreText"/>
/// and <see cref="CentreCaption"/> to use it, or leave them blank for a plain ring.
/// </remarks>
public sealed class PieChart : FrameworkElement
{
    private const double LegendWidth = 150;
    private const double LegendRowHeight = 20;
    private const double SwatchSize = 10;
    private const double MinDiameter = 40;

    public static readonly DependencyProperty SlicesProperty = DependencyProperty.Register(
        nameof(Slices),
        typeof(IReadOnlyList<ChartSlice>),
        typeof(PieChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CentreTextProperty = DependencyProperty.Register(
        nameof(CentreText),
        typeof(string),
        typeof(PieChart),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CentreCaptionProperty = DependencyProperty.Register(
        nameof(CentreCaption),
        typeof(string),
        typeof(PieChart),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<ChartSlice>? Slices
    {
        get => (IReadOnlyList<ChartSlice>?)GetValue(SlicesProperty);
        set => SetValue(SlicesProperty, value);
    }

    /// <summary>The headline written in the hole — usually the total the wedges add up to.</summary>
    public string CentreText
    {
        get => (string)GetValue(CentreTextProperty);
        set => SetValue(CentreTextProperty, value);
    }

    /// <summary>The small line under <see cref="CentreText"/> saying what it is.</summary>
    public string CentreCaption
    {
        get => (string)GetValue(CentreCaptionProperty);
        set => SetValue(CentreCaptionProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        base.OnRender(drawingContext);

        var slices = (Slices ?? Array.Empty<ChartSlice>())
            .Where(slice => slice.Value > 0)
            .ToList();

        if (slices.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0)
            return;

        var total = slices.Sum(slice => slice.Value);
        var legend = Math.Min(LegendWidth, ActualWidth * 0.45);
        var diameter = Math.Min(ActualHeight, ActualWidth - legend);
        if (diameter < MinDiameter)
            return;

        var radius = diameter / 2;
        var centre = new Point(radius, ActualHeight / 2);

        DrawWedges(drawingContext, slices, total, centre, radius);
        DrawCentre(drawingContext, centre);
        DrawLegend(drawingContext, slices, total, diameter, legend);
    }

    private static void DrawWedges(
        DrawingContext drawingContext, List<ChartSlice> slices, double total, Point centre, double radius)
    {
        var inner = radius * 0.58;
        var angle = -Math.PI / 2;   // start at twelve o'clock, which is where a reader starts

        foreach (var slice in slices)
        {
            var sweep = 2 * Math.PI * (slice.Value / total);

            // A full circle cannot be expressed as a single arc — its start and end points are the
            // same, so the arc renderer has no way to tell 0° from 360° and draws nothing. One
            // slice is therefore a ring, not a wedge.
            if (slices.Count == 1)
            {
                drawingContext.DrawGeometry(slice.Fill, null, Ring(centre, radius, inner));
                return;
            }

            drawingContext.DrawGeometry(slice.Fill, null, Wedge(centre, radius, inner, angle, angle + sweep));
            angle += sweep;
        }
    }

    private void DrawCentre(DrawingContext drawingContext, Point centre)
    {
        if (string.IsNullOrWhiteSpace(CentreText))
            return;

        var headline = ChartText.Make(CentreText, 15, ChartText.Strong, this, bold: true);
        var caption = string.IsNullOrWhiteSpace(CentreCaption)
            ? null
            : ChartText.Make(CentreCaption, 10, ChartText.Muted, this);

        var block = headline.Height + (caption?.Height ?? 0);
        var y = centre.Y - (block / 2);

        drawingContext.DrawText(headline, new Point(centre.X - (headline.Width / 2), y));
        if (caption is not null)
            drawingContext.DrawText(caption, new Point(centre.X - (caption.Width / 2), y + headline.Height));
    }

    private void DrawLegend(
        DrawingContext drawingContext, List<ChartSlice> slices, double total, double diameter, double legendWidth)
    {
        var rows = Math.Min(slices.Count, (int)(ActualHeight / LegendRowHeight));
        var top = (ActualHeight - (rows * LegendRowHeight)) / 2;
        var left = diameter + 12;

        for (var i = 0; i < rows; i++)
        {
            var slice = slices[i];
            var y = top + (i * LegendRowHeight);

            drawingContext.DrawRoundedRectangle(
                slice.Fill, null, new Rect(left, y + 4, SwatchSize, SwatchSize), 2, 2);

            var share = slice.Value / total;
            var text = ChartText.Fit(
                $"{slice.Label}  {share:P0}", 11, ChartText.Muted, this, legendWidth - SwatchSize - 18);

            drawingContext.DrawText(text, new Point(left + SwatchSize + 6, y + 1));
        }
    }

    private static Geometry Ring(Point centre, double outer, double inner)
    {
        var geometry = new CombinedGeometry(
            GeometryCombineMode.Exclude,
            new EllipseGeometry(centre, outer, outer),
            new EllipseGeometry(centre, inner, inner));

        geometry.Freeze();
        return geometry;
    }

    private static Geometry Wedge(Point centre, double outer, double inner, double from, double to)
    {
        var isLarge = (to - from) > Math.PI;

        var outerFrom = OnCircle(centre, outer, from);
        var outerTo = OnCircle(centre, outer, to);
        var innerTo = OnCircle(centre, inner, to);
        var innerFrom = OnCircle(centre, inner, from);

        var figure = new PathFigure { StartPoint = outerFrom, IsClosed = true, IsFilled = true };
        figure.Segments.Add(new ArcSegment(outerTo, new Size(outer, outer), 0, isLarge, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(innerTo, true));
        figure.Segments.Add(new ArcSegment(innerFrom, new Size(inner, inner), 0, isLarge, SweepDirection.Counterclockwise, true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    private static Point OnCircle(Point centre, double radius, double angle)
        => new(centre.X + (radius * Math.Cos(angle)), centre.Y + (radius * Math.Sin(angle)));
}
