using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CameywareOrder.Converters;

[ValueConversion(typeof(object), typeof(Visibility))]
public class NullToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// null  → Collapsed  (default)
    /// value → Visible
    /// Pass ConverterParameter="Invert" to flip behaviour.
    /// </summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isNull = value is null;
        bool invert = parameter?.ToString() == "Invert";
        bool show = invert ? isNull : !isNull;
        return show ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
