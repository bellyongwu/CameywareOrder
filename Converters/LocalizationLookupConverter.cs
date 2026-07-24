using System.Globalization;
using System.Windows.Data;
using LeeYongeOrdering.Localization;

namespace LeeYongeOrdering.Converters;

[ValueConversion(typeof(object), typeof(string))]
public class LocalizationLookupConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is not string prefix || string.IsNullOrWhiteSpace(prefix))
            return string.Empty;

        var suffix = value.ToString();
        if (string.IsNullOrWhiteSpace(suffix))
            return string.Empty;

        var key = $"{prefix}.{suffix}";
        var localized = LocalizationService.Instance[key];
        return string.Equals(localized, key, StringComparison.Ordinal) ? suffix : localized;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
