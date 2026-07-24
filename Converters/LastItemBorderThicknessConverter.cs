using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LeeYongeOrdering.Converters;

/// <summary>
/// Produces a bottom border for every item in a list except the last one.
/// values[0] = the current item, values[1] = the owning ItemsControl.Items collection.
/// </summary>
public class LastItemBorderThicknessConverter : IMultiValueConverter
{
    private static readonly Thickness WithLine = new(0, 0, 0, 1);
    private static readonly Thickness NoLine = new(0);

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[1] is not IEnumerable collection)
            return WithLine;

        var current = values[0];
        object? last = null;
        foreach (var element in collection)
            last = element;

        return Equals(current, last) ? NoLine : WithLine;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
