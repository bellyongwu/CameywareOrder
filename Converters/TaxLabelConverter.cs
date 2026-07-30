using System.Globalization;
using System.Windows.Data;
using CameywareOrder.Localization;
using CameywareOrder.Models;
using CameywareOrder.Services;

namespace CameywareOrder.Converters;

/// <summary>
/// Names an order's tax for the pricing mode it was quoted in. Bind it to the <see cref="Order"/>.
/// </summary>
/// <remarks>
/// Tax ADDED at settlement is just "tax", and a panel reading subtotal + tax = total explains itself.
/// Tax already INSIDE the price has to say so, because the same three rows then read
/// "subtotal 1000 / tax 90.91 / total 1000" — arithmetic that looks broken unless the middle row
/// admits it was carved out of the last one rather than added to the first.
///
/// And it has to say WHICH tax and at WHAT rate, because that is the question a customer holding the
/// receipt actually asks. The name comes from the shop's jurisdiction (a <c>TaxName.*</c> key — VAT
/// in China and the EU, a consumption tax in Japan); the rate comes from the ORDER, so a receipt
/// reprinted after a government moves the rate still quotes what it charged.
///
/// Bound to the order rather than to <c>Order.PricesIncludeTax</c>, which is what it used to take:
/// a bool can pick between two fixed words but cannot name a rate. <see cref="Label"/> is public and
/// static so the printed receipt renders the identical string — the receipt is the version a customer
/// keeps, and one that disagrees with the screen is the one that gets questioned.
/// </remarks>
public sealed class TaxLabelConverter : IValueConverter
{
    /// <summary>What to call this order's tax, in the current language.</summary>
    public static string Label(Order? order)
    {
        var localization = LocalizationService.Instance;

        if (order is null || !order.PricesIncludeTax)
            return localization["Order.Fields.TaxAmount"];

        var rate = order.IncludedTaxRatePercent;
        if (rate <= 0m)
            return localization["Order.Fields.IncludedTax"];

        return localization.Format("Order.Fields.IncludedTaxLabel",
            TaxJurisdictions.TaxName(ShopContext.Instance.Current, localization),
            TaxRateFormat.Text(rate));
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Label(value as Order);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
