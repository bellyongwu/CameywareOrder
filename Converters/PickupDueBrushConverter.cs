using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using CameywareOrder.Models;

namespace CameywareOrder.Converters;

/// <summary>
/// Tints an order row by how close it is to the pickup day it was promised for: amber inside two
/// weeks, red once the day has passed, nothing otherwise.
/// </summary>
/// <remarks>
/// Binds the whole <see cref="Order"/> rather than a computed brush on the model, for the same
/// reason <c>BalanceStatusColorConverter</c> does: a colour is a fact about the SCREEN, and putting
/// brushes on an entity drags WPF types into a class the GraphQL server and the printer also use.
///
/// The brushes are deliberately PALE and semi-transparent. A row is a line of text first — a
/// saturated orange behind it costs more legibility than the warning is worth — and the transparency
/// is what lets the hover shade underneath still show through, so a tinted row does not stop
/// responding to the mouse. They are frozen: this runs once per row per rebuild.
/// </remarks>
public sealed class PickupDueBrushConverter : IValueConverter
{
    private static readonly Brush SoonBrush = Frozen(Color.FromArgb(0x8C, 0xFF, 0xE0, 0xB2));
    private static readonly Brush OverdueBrush = Frozen(Color.FromArgb(0x8C, 0xFF, 0xCD, 0xD2));

    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is Order order
            ? order.PickupDue switch
            {
                PickupDueKind.Soon => SoonBrush,
                PickupDueKind.Overdue => OverdueBrush,
                _ => Brushes.Transparent
            }
            : Brushes.Transparent;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
