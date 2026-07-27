using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CameywareOrder.Converters;

[ValueConversion(typeof(decimal), typeof(Visibility))]
public class PositiveAmountToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// amount &gt; 0 → Visible; otherwise Collapsed.
    /// </summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var amount = value switch
        {
            decimal d => d,
            double db => (decimal)db,
            int i => i,
            _ => 0m
        };

        return amount > 0m ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
