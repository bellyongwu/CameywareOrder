using System.Globalization;
using System.Windows.Data;
using CameywareOrder.Models;

namespace CameywareOrder.Converters;

/// <summary>
/// Renders an amount with its currency symbol. Takes the amount as <c>values[0]</c> and the
/// <see cref="CurrencyType"/> the amount is denominated in as <c>values[1]</c>.
/// </summary>
/// <remarks>
/// The second value is the whole point and every call site in the XAML already supplied it — this
/// converter read <c>CurrencySettingService.Instance.Symbol</c> instead and threw it away. That was
/// invisible while a shop had exactly one currency and became wrong the moment one could accept two:
/// a ￥1,695 order would have re-rendered as "$1,695.00" the instant the branch started taking
/// dollars, because the SHOP's setting is a statement about today and the ORDER's is a fact about
/// when it was priced.
///
/// A missing or unrecognised second value falls back to the shop's current currency rather than to
/// dollars, which keeps a stale binding rendering the same as it did before instead of silently
/// asserting the wrong money.
/// </remarks>
public class CurrencyAmountConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 1)
            return string.Empty;

        var amount = ParseAmount(values[0]);
        var currency = values.Length > 1 && values[1] is CurrencyType supplied
            ? supplied
            : Services.CurrencySettingService.Instance.Current;

        return Services.CurrencySettingService.Format(amount, currency);
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
