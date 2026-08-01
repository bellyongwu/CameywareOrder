using CameywareOrder.Models;

namespace CameywareOrder.Services;

/// <summary>
/// Turns a set of orders into a <see cref="SettlementReport"/> for a period.
/// </summary>
/// <remarks>
/// <b>Pure.</b> It takes orders and gives back numbers: no database, no WPF, no string table, no
/// singletons. That is what lets the window, the PDF and anything added later read the SAME figures
/// rather than each computing its own — and it is what makes the arithmetic testable without opening
/// anything.
///
/// <b>It re-computes nothing.</b> Every figure comes from the money the order already carries
/// (<c>Order.MoneyFor</c> → <c>SectionPayment</c>). The two pricing modes, the per-portion tax rules
/// and the deposit clamp all live in <c>Order.CalculateSectionPayment</c>, and a report that
/// re-derived any of them would be a second implementation free to disagree with the receipt the
/// customer is holding.
/// </remarks>
public static class SettlementCalculator
{
    private static readonly ServiceLine[] AllLines =
    {
        ServiceLine.Alterations, ServiceLine.CustomMade, ServiceLine.Clothing
    };

    /// <summary>
    /// The report for <paramref name="period"/>, built from the orders DATED inside it.
    /// </summary>
    /// <param name="orders">Any set of orders; the ones outside the period are ignored.</param>
    /// <param name="period">The period, in the shop's local days.</param>
    /// <param name="currency">What the figures are denominated in — for the caller to render.</param>
    /// <remarks>
    /// Filtered on <c>OrderDate</c>: an order belongs to the month it was TAKEN in, which is the
    /// question "what did August earn" asks. Not the pickup date (work promised is not money) and not
    /// last-modified (which would move an order between months every time somebody opened it).
    /// </remarks>
    public static SettlementReport For(IEnumerable<Order> orders, DateRange period, CurrencyType currency)
    {
        ArgumentNullException.ThrowIfNull(orders);

        var inPeriod = orders.Where(order => period.Contains(order.OrderDate)).ToList();

        // Refunded orders are counted but earn nothing — see SettlementReport's remarks. Their value
        // is reported on its own so the money is disclosed rather than merely dropped.
        var earning = inPeriod.Where(order => !order.IsRefunded).ToList();
        var refunded = inPeriod.Where(order => order.IsRefunded).ToList();

        return new SettlementReport(
            period,
            CountStates(inPeriod),
            AllLines.Select(line => TotalsFor(earning, line)).ToList(),
            MethodTotalsFor(earning),
            refunded.Sum(order => order.ComputedSectionsTotal),
            currency);
    }

    private static OrderStateCounts CountStates(IReadOnlyCollection<Order> orders) => new(
        Total: orders.Count,
        // "Not finished" is anything still to be worked: neither handed over nor refunded.
        Unfinished: orders.Count(order => !order.IsPickedUp && !order.IsRefunded),
        Completed: orders.Count(order => order.Status == OrderStatus.Completed),
        Shipped: orders.Count(order => order.Status == OrderStatus.Shipped),
        Cancelled: orders.Count(order => order.Status == OrderStatus.Cancelled),
        Returned: orders.Count(order => order.Status == OrderStatus.Returned));

    private static ServiceLineTotals TotalsFor(IReadOnlyCollection<Order> orders, ServiceLine line)
    {
        decimal preTax = 0m, tax = 0m, received = 0m, outstanding = 0m;
        var count = 0;

        foreach (var order in orders)
        {
            var money = order.MoneyFor(line);
            if (money.Total <= 0m)
                continue;

            count++;
            // Subtotal is the pre-tax base in BOTH pricing modes — inclusive prices have the tax
            // taken back out of them by CalculateSectionPayment, which is exactly why this does not
            // try to work it out from Total.
            preTax += money.Subtotal;
            tax += money.Tax;
            received += order.ReceivedFor(line);
            outstanding += order.OutstandingFor(line);
        }

        return new ServiceLineTotals(line, preTax, tax, received, outstanding, count);
    }

    /// <summary>
    /// What each payment method took, over every line and both stages.
    /// </summary>
    /// <remarks>
    /// <b>The stage total is authoritative; the split only says how to divide it.</b> A split stage's
    /// lines are pre-tax amounts, so summing them would miss the tax and leave the method figures
    /// short of the received total. Instead the stage's KNOWN received amount is apportioned across
    /// its lines by their share — which keeps the invariant that matters on a settlement sheet:
    /// cash + card + transfer + … equals the money received. <c>settlementcheck</c> asserts it.
    /// </remarks>
    private static IReadOnlyList<MethodTotals> MethodTotalsFor(IReadOnlyCollection<Order> orders)
    {
        var received = new Dictionary<PaymentMethod, decimal>();
        var payments = new Dictionary<PaymentMethod, int>();

        void Add(PaymentMethod? method, decimal amount)
        {
            if (amount <= 0m)
                return;

            // An order that recorded no method still took money; attributing it to None keeps the
            // methods adding up to the total instead of quietly losing the difference.
            var key = method ?? PaymentMethod.None;
            received[key] = received.GetValueOrDefault(key) + amount;
            payments[key] = payments.GetValueOrDefault(key) + 1;
        }

        foreach (var order in orders)
        {
            foreach (var line in AllLines)
            {
                var money = order.MoneyFor(line);
                if (money.Total <= 0m)
                    continue;

                var split = order.SplitFor(line);
                Distribute(Add, money.ReceivedDownpayment, split, finalStage: false, order, line);

                var settledFinal = order.ReceivedFor(line) - money.ReceivedDownpayment;
                Distribute(Add, settledFinal, split, finalStage: true, order, line);
            }
        }

        return received
            .Select(entry => new MethodTotals(entry.Key, entry.Value, payments[entry.Key]))
            .OrderByDescending(entry => entry.Received)
            .ToList();
    }

    private static void Distribute(
        Action<PaymentMethod?, decimal> add,
        decimal stageTotal,
        SectionPaymentSplit split,
        bool finalStage,
        Order order,
        ServiceLine line)
    {
        if (stageTotal <= 0m)
            return;

        var lines = split.IsEnabled(finalStage) ? split.Charged(finalStage) : null;
        var declared = lines?.Sum(entry => entry.Amount) ?? 0m;

        if (lines is null || lines.Count == 0 || declared <= 0m)
        {
            add(order.MethodFor(line, finalStage), stageTotal);
            return;
        }

        // Apportion by share, and give the LAST line whatever rounding left over, so the parts add
        // up to the stage total exactly rather than to within a cent of it.
        var remaining = stageTotal;
        for (var i = 0; i < lines.Count; i++)
        {
            var amount = i == lines.Count - 1
                ? remaining
                : Math.Round(stageTotal * (lines[i].Amount / declared), 2, MidpointRounding.AwayFromZero);

            add(lines[i].Method, amount);
            remaining -= amount;
        }
    }
}
