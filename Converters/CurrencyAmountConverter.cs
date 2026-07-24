using System.Globalization;
using System.Windows.Data;
using LeeYongeOrdering.Models;

namespace LeeYongeOrdering.Converters;

public class CurrencyAmountConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
            return string.Empty;

        var amount = ParseAmount(values[0]);
        var currencyType = ParseCurrency(values[1]);
        var symbol = currencyType == CurrencyType.CNY ? "￥" : "$";

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

    private static CurrencyType ParseCurrency(object? value)
    {
        if (value is CurrencyType currencyType)
            return currencyType;

        return Enum.TryParse<CurrencyType>(value?.ToString(), out var parsed)
            ? parsed
            : CurrencyType.CAD;
    }
}
