using System.Globalization;
using System.Windows.Data;

namespace CameywareOrder.Converters;

public class CurrencyAmountConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 1)
            return string.Empty;

        var amount = ParseAmount(values[0]);
        var symbol = Services.CurrencySettingService.Instance.Symbol;

        return $"{symbol}{amount:N2}";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static decimal ParseAmount(object? value)
    {
        if (value is decimal decimalValue)
            return decimalValue;

        if (value is double doubleValue)
            return (decimal)doubleValue;

        if (value is float floatValue)
            return (decimal)floatValue;

        return decimal.TryParse(value?.ToString(), out var parsed) ? parsed : 0m;
    }
}
