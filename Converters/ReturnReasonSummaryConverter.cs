using System.Globalization;
using System.Windows.Data;
using CameywareOrder.Localization;

namespace CameywareOrder.Converters;

// Resolves the cancellation/return reason shown in the order-details panel and the
// receipt: values[0] = Order.StatusReasonCategory (stable key, e.g. "CustomerDoesNotWant"
// or "Other"), values[1] = Order.StatusReason (free text, only meaningful for "Other").
public class ReturnReasonSummaryConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var category = values.Length > 0 ? values[0] as string : null;
        var freeText = values.Length > 1 ? values[1] as string : null;
        return Resolve(category, freeText);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    /// <summary>Shared resolution logic reused by receipt/PDF code-behind (see MainWindow).</summary>
    public static string Resolve(string? category, string? freeText)
    {
        if (string.IsNullOrWhiteSpace(category) || category == "Other")
            return string.IsNullOrWhiteSpace(freeText) ? "-" : freeText;

        return LocalizationService.Instance[$"ReturnReason.{category}"];
    }
}
