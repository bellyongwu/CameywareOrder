using System.Globalization;
using System.Windows.Data;
using CameywareOrder.Localization;

namespace CameywareOrder.Converters;

/// <summary>
/// Names an order's tax for the pricing mode it was quoted in. Bind it to
/// <c>Order.PricesIncludeTax</c>.
/// </summary>
/// <remarks>
/// Tax ADDED at settlement is just "tax", and a panel reading subtotal + tax = total explains itself.
/// Tax already INSIDE the price has to say so, because the same three rows then read
/// "subtotal 1000 / tax 90.91 / total 1000" — arithmetic that looks broken unless the middle row
/// admits it was carved out of the last one rather than added to the first. Same reason the receipt's
/// totals block picks between the two labels.
/// </remarks>
public sealed class TaxLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => LocalizationService.Instance[value is true ? "Order.Fields.IncludedTax" : "Order.Fields.TaxAmount"];

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
