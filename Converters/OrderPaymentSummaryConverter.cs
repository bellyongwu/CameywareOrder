using System.Globalization;
using System.Text;
using System.Windows.Data;
using LeeYongeOrdering.Localization;
using LeeYongeOrdering.Models;

namespace LeeYongeOrdering.Converters;

/// <summary>
/// Renders a per-service payment breakdown or an overall balance status for an
/// <see cref="Order"/>. Pass ConverterParameter="Status" for the cleared/outstanding
/// indicator, otherwise a multi-line payment breakdown is produced.
/// </summary>
public class OrderPaymentSummaryConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Order order)
            return string.Empty;

        var loc = LocalizationService.Instance;
        var mode = parameter as string ?? "Breakdown";

        if (string.Equals(mode, "Status", StringComparison.OrdinalIgnoreCase))
        {
            return order.IsBalanceCleared
                ? loc["Payment.Status.Cleared"]
                : loc["Payment.Status.Outstanding"];
        }

        var symbol = order.CurrencyType == CurrencyType.CNY ? "￥" : "$";
        var builder = new StringBuilder();

        AppendSection(builder, loc, symbol, new PaymentSection("ServiceType.Alterations",
            order.AlterationTotal, order.AlterationDownpaymentMethod, order.AlterationDownpayment, order.AlterationFinalBalanceMethod, order.AlterationSectionCleared));
        AppendSection(builder, loc, symbol, new PaymentSection("ServiceType.CustomMade",
            order.CustomMadeTotal, order.CustomMadeDownpaymentMethod, order.CustomMadeDownpayment, order.CustomMadeFinalBalanceMethod, order.CustomMadeSectionCleared));
        AppendSection(builder, loc, symbol, new PaymentSection("ServiceType.ReadyMade",
            order.ClothingTotal, order.ClothingDownpaymentMethod, order.ClothingDownpayment, order.ClothingFinalBalanceMethod, order.ClothingSectionCleared));

        return builder.Length == 0 ? "-" : builder.ToString().TrimEnd();
    }

    private readonly record struct PaymentSection(
        string ServiceKey,
        decimal SectionTotal,
        PaymentMethod? DownMethod,
        decimal? DownAmount,
        PaymentMethod? FinalMethod,
        bool Cleared);

    private static void AppendSection(StringBuilder builder, LocalizationService loc, string symbol, PaymentSection section)
    {
        if (section.SectionTotal <= 0m && section.DownMethod is null && section.FinalMethod is null)
            return;

        builder.Append(loc[section.ServiceKey]).Append(": ");
        builder.Append(loc["Order.Fields.Downpayment"]).Append(' ');
        builder.Append(MethodText(loc, section.DownMethod)).Append(' ').Append(symbol).Append((section.DownAmount ?? 0m).ToString("N2"));
        builder.Append("  |  ");
        builder.Append(loc["Order.Fields.FinalBalanceMethod"]).Append(' ').Append(MethodText(loc, section.FinalMethod));
        builder.Append("  [")
            .Append(section.Cleared ? loc["Payment.Status.Cleared"] : loc["Payment.Status.Outstanding"])
            .Append(']');
        builder.AppendLine();
    }

    private static string MethodText(LocalizationService loc, PaymentMethod? method)
        => method is null ? "-" : loc[$"PaymentMethod.{method}"];

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
