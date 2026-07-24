using System.Globalization;
using System.Windows.Data;
using LeeYongeOrdering.Localization;
using LeeYongeOrdering.Models;

namespace LeeYongeOrdering.Converters;

[ValueConversion(typeof(OrderStatus), typeof(string))]
public class OrderStatusToLocalizedTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not OrderStatus status)
            return LocalizationService.Instance["Filter.Status.All"];

        return LocalizationService.Instance[$"Status.{status}"];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
