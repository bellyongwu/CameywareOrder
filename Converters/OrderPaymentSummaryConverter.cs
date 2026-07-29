using System.Globalization;
using System.Text;
using System.Windows.Data;
using CameywareOrder.Localization;
using CameywareOrder.Models;

namespace CameywareOrder.Converters;

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
            return loc[order.PaymentStatusKind switch
            {
                BalanceStatusKind.Refunded => "Payment.Status.Refunded",
                BalanceStatusKind.ClearedPickedUp => "Payment.Status.ClearedPickedUp",
                BalanceStatusKind.ClearedNotPickedUp => "Payment.Status.ClearedNotPickedUp",
                _ => "Payment.Status.Outstanding"
            }];
        }

        // The order's own currency, not the shop's current one: this breakdown is a statement about
        // money that was already taken, and the shop may since have started accepting another.
        var currency = order.CurrencyType;
        var builder = new StringBuilder();

        AppendSection(builder, loc, currency, new PaymentSection("ServiceType.Alterations",
            order.AlterationMoney, order.AlterationDownpaymentMethod, order.AlterationFinalBalanceMethod, order.AlterationSectionCleared));
        AppendSection(builder, loc, currency, new PaymentSection("ServiceType.CustomMade",
            order.CustomMadeMoney, order.CustomMadeDownpaymentMethod, order.CustomMadeFinalBalanceMethod, order.CustomMadeSectionCleared));
        AppendSection(builder, loc, currency, new PaymentSection("ServiceType.ReadyMade",
            order.ClothingMoney, order.ClothingDownpaymentMethod, order.ClothingFinalBalanceMethod, order.ClothingSectionCleared));

        return builder.Length == 0 ? "-" : builder.ToString().TrimEnd();
    }

    private readonly record struct PaymentSection(
        string ServiceKey,
        SectionPayment Money,
        PaymentMethod? DownMethod,
        PaymentMethod? FinalMethod,
        bool Cleared);

    private static void AppendSection(StringBuilder builder, LocalizationService loc, CurrencyType currency, PaymentSection section)
    {
        var money = section.Money;
        if (money.Total <= 0m && section.DownMethod is null && section.FinalMethod is null)
            return;

        var depositTax = money.ReceivedDownpayment - money.Deposit;
        var finalTax = money.FinalCharge - money.FinalBase;
        var taxLabel = loc["Order.Fields.TaxAmount"];

        builder.Append(loc[section.ServiceKey]).Append(": ");
        // Deposit portion: method, base amount and the tax charged on it.
        builder.Append(loc["Order.Fields.Downpayment"]).Append(' ');
        builder.Append(MethodText(loc, section.DownMethod)).Append(' ').Append(Services.CurrencySettingService.Format(money.Deposit, currency));
        builder.Append(" (").Append(taxLabel).Append(' ').Append(Services.CurrencySettingService.Format(depositTax, currency)).Append(')');
        builder.Append("  |  ");
        // Final balance portion: method, base amount and the tax charged on it.
        builder.Append(loc["Order.Fields.FinalBalanceShort"]).Append(' ');
        builder.Append(MethodText(loc, section.FinalMethod)).Append(' ').Append(Services.CurrencySettingService.Format(money.FinalBase, currency));
        builder.Append(" (").Append(taxLabel).Append(' ').Append(Services.CurrencySettingService.Format(finalTax, currency)).Append(')');
        builder.Append("  [")
            .Append(section.Cleared ? loc["Payment.Status.Cleared"] : loc["Payment.Status.Outstanding"])
            .Append(']');
        builder.AppendLine();
    }

    // Normalized so an order still holding the legacy single "Card" value reads as Debit Card,
    // which is what that option was labelled before debit and credit were separated.
    private static string MethodText(LocalizationService loc, PaymentMethod? method)
        => method is null ? "-" : loc[$"PaymentMethod.{PaymentTaxRules.Normalize(method.Value)}"];

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
