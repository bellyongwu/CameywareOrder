using System.Globalization;
using System.Windows.Data;
using CameywareOrder.Localization;
using CameywareOrder.Models;

namespace CameywareOrder.Converters;

/// <summary>
/// Lists every service that is actually present in an order (has a charge, details,
/// items or custom-made records) instead of just the single primary service type.
/// </summary>
[ValueConversion(typeof(Order), typeof(string))]
public class OrderServicesSummaryConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Order order)
            return string.Empty;

        var loc = LocalizationService.Instance;
        var services = new List<string>();

        if (order.AlterationTotal > 0m || !string.IsNullOrWhiteSpace(order.ServiceDetails))
            services.Add(loc["ServiceType.Alterations"]);

        if (order.CustomMadeTotal > 0m || order.CustomMadeRecords.Count > 0)
            services.Add(loc["ServiceType.CustomMade"]);

        if (order.ClothingTotal > 0m || order.Items.Count > 0)
            services.Add(loc["ServiceType.ReadyMade"]);

        // Fall back to the stored primary service type when nothing else is detected.
        if (services.Count == 0)
        {
            var key = $"ServiceType.{order.ServiceType}";
            var localized = loc[key];
            services.Add(string.Equals(localized, key, StringComparison.Ordinal) ? order.ServiceType.ToString() : localized);
        }

        return string.Join("、", services);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
