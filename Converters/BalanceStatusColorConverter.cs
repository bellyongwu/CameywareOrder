using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using LeeYongeOrdering.Models;

namespace LeeYongeOrdering.Converters;

/// <summary>
/// Maps an <see cref="Order"/>'s <see cref="Order.PaymentStatusKind"/> to the brush
/// used for the balance-status label: green (cleared + picked up), light green
/// (cleared, not picked up), orange (outstanding) and red (refunded).
/// </summary>
public class BalanceStatusColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Order order)
            return Brushes.Black;

        var color = order.PaymentStatusKind switch
        {
            BalanceStatusKind.ClearedPickedUp => "#2E7D32",
            BalanceStatusKind.ClearedNotPickedUp => "#66BB6A",
            BalanceStatusKind.Refunded => "#C62828",
            _ => "#EF6C00"
        };

        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
