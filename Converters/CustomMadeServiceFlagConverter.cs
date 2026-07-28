using System.Globalization;
using System.Windows;
using System.Windows.Data;
using CameywareOrder.Localization;
using CameywareOrder.Models;
using CameywareOrder.Services;

namespace CameywareOrder.Converters;

// Drives the "定制服务" list column. Bound to the whole Order row; the ConverterParameter
// selects what to emit:
//   "Flag"           -> localized 有 / 无 (has custom-made measurements or not)
//   "Names"          -> the bracketed garment-name line, e.g. (西装、衬衣), or empty
//   "NamesVisibility"-> Visible when there are garment names, else Collapsed
[ValueConversion(typeof(Order), typeof(string))]
public class CustomMadeServiceFlagConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var mode = parameter as string ?? "Flag";

        if (value is not Order order)
            return string.Equals(mode, "NamesVisibility", StringComparison.Ordinal)
                ? Visibility.Collapsed
                : string.Empty;

        var languageCode = LocalizationService.Instance.CurrentLanguageCode;
        var names = CustomMadeMeasurementReader.GetGarmentNames(order, languageCode);
        var hasNames = names.Count > 0;

        if (string.Equals(mode, "NamesVisibility", StringComparison.Ordinal))
            return hasNames ? Visibility.Visible : Visibility.Collapsed;

        if (string.Equals(mode, "Names", StringComparison.Ordinal))
        {
            if (!hasNames)
                return string.Empty;

            return $"({LocalizationService.Instance.JoinList(names, languageCode)})";
        }

        return LocalizationService.Instance[hasNames ? "CustomMade.Flag.Yes" : "CustomMade.Flag.No"];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
