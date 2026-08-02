namespace CameywareOrder.Models;

/// <summary>The three service lines a shop earns from, as a report groups them.</summary>
public enum ServiceLine
{
    /// <summary>Altering a garment the customer already owns.</summary>
    Alterations,

    /// <summary>Made to measure.</summary>
    CustomMade,

    /// <summary>Selling ready-made stock.</summary>
    Clothing
}

/// <summary>What the three service lines are called, and the order they are reported in.</summary>
/// <remarks>
/// One owner for both facts. The name mapping used to be a private switch inside
/// <c>SettlementWindow</c>; the CSV export is the second consumer, and a second copy would let the
/// spreadsheet head a column with a different word from the report beside it — the classic way a
/// figure comes to be called two things depending on which screen produced it.
///
/// A switch rather than <c>$"ServiceLine.{line}"</c> so a line added to the enum fails to COMPILE
/// here rather than silently rendering its raw name on a printed report. The keys are the
/// <c>ServiceType.*</c> ones the rest of the application already uses for the same three things —
/// note <see cref="ServiceLine.Clothing"/> is <c>ServiceType.ReadyMade</c>, which is why this cannot
/// be derived from the value's name.
/// </remarks>
public static class ServiceLines
{
    /// <summary>The three lines, in reporting order.</summary>
    public static readonly IReadOnlyList<ServiceLine> All = new[]
    {
        ServiceLine.Alterations, ServiceLine.CustomMade, ServiceLine.Clothing
    };

    /// <summary>The string-table key naming a line.</summary>
    public static string NameKey(ServiceLine line) => line switch
    {
        ServiceLine.Alterations => "ServiceType.Alterations",
        ServiceLine.CustomMade => "ServiceType.CustomMade",
        ServiceLine.Clothing => "ServiceType.ReadyMade",
        _ => "ServiceType.Alterations"
    };
}

/// <summary>One service line's money over a period.</summary>
/// <param name="Line">Which line.</param>
/// <param name="PreTax">Charged before tax.</param>
/// <param name="Tax">Tax on it.</param>
/// <param name="Received">Actually taken so far — deposits plus settled balances.</param>
/// <param name="Outstanding">Still owed.</param>
/// <param name="OrderCount">Orders carrying a charge on this line.</param>
/// <remarks>
/// <see cref="PostTax"/> is derived rather than stored so it cannot disagree with its two parts.
/// In a tax-INCLUSIVE market the tax is already inside the price, so <see cref="PreTax"/> is the
/// price net of it and the sum still holds — that is the whole reason the figures come from
/// <c>SectionPayment</c> rather than being re-derived here.
/// </remarks>
public sealed record ServiceLineTotals(
    ServiceLine Line,
    decimal PreTax,
    decimal Tax,
    decimal Received,
    decimal Outstanding,
    int OrderCount)
{
    public decimal PostTax => PreTax + Tax;

    public static ServiceLineTotals Empty(ServiceLine line) => new(line, 0m, 0m, 0m, 0m, 0);
}

/// <summary>What was taken by one payment method.</summary>
public sealed record MethodTotals(PaymentMethod Method, decimal Received, int PaymentCount);

/// <summary>How many orders were in each state at the end of the period.</summary>
/// <remarks>
/// <see cref="Unfinished"/> is the count still to be worked — the question "how many orders are not
/// finished" asks. Cancelled and returned are kept APART from each other: they are both refunds but
/// a shop reads them differently, one being work never started and the other work handed back.
/// </remarks>
public sealed record OrderStateCounts(
    int Total,
    int Unfinished,
    int Completed,
    int Shipped,
    int Cancelled,
    int Returned)
{
    /// <summary>Cancelled plus returned — the orders that earned nothing.</summary>
    public int Refunded => Cancelled + Returned;

    /// <summary>Orders that produced revenue: everything that was not refunded.</summary>
    public int Earning => Total - Refunded;
}

/// <summary>
/// A shop's takings over a period, ready to be shown, printed or exported.
/// </summary>
/// <remarks>
/// <b>Plain data.</b> No WPF, no database, no string table — every figure is a number and every label
/// is the caller's problem, so the same report object drives the window, the PDF and anything added
/// later. It is built by <c>SettlementCalculator</c> and nothing else constructs one.
///
/// <b>Refunded orders earn nothing.</b> Cancelled and returned orders are COUNTED (a shop needs to
/// know how many) but contribute no money to any total: their takings went back to the customer, and
/// including them would overstate every line on the report. They are reported separately, with the
/// value that was reversed, so the number is not merely hidden.
/// </remarks>
public sealed record SettlementReport(
    DateRange Period,
    OrderStateCounts Counts,
    IReadOnlyList<ServiceLineTotals> Lines,
    IReadOnlyList<MethodTotals> Methods,
    decimal RefundedValue,
    CurrencyType Currency)
{
    /// <summary>Charged before tax, across every service line.</summary>
    public decimal PreTaxTotal => Lines.Sum(line => line.PreTax);

    /// <summary>Tax charged, across every service line — the "service tax collected" figure.</summary>
    public decimal TaxTotal => Lines.Sum(line => line.Tax);

    /// <summary>The bottom line: what the work is worth with tax.</summary>
    public decimal PostTaxTotal => PreTaxTotal + TaxTotal;

    /// <summary>Money actually in the till — deposits plus settled balances.</summary>
    public decimal ReceivedTotal => Lines.Sum(line => line.Received);

    /// <summary>Still to be collected.</summary>
    public decimal OutstandingTotal => Lines.Sum(line => line.Outstanding);

    /// <summary>Taken in cash.</summary>
    public decimal CashReceived => ReceivedBy(PaymentMethod.Cash);

    /// <summary>
    /// Taken by card — debit, credit, and the legacy undifferentiated <c>Card</c> together.
    /// </summary>
    /// <remarks>
    /// The legacy value has to be included or every order saved before the debit/credit split
    /// silently drops out of the card figure while still counting in the total, and the two stop
    /// adding up. <c>PaymentMethod.Card</c> is never deleted for exactly this reason.
    /// </remarks>
    public decimal CardReceived
        => ReceivedBy(PaymentMethod.DebitCard) + ReceivedBy(PaymentMethod.CreditCard) + ReceivedBy(PaymentMethod.Card);

    /// <summary>Taken by bank transfer.</summary>
    public decimal TransferReceived => ReceivedBy(PaymentMethod.Etransfer);

    /// <summary>One line's totals, or an empty set when the period has nothing on it.</summary>
    public ServiceLineTotals Line(ServiceLine line)
        => Lines.FirstOrDefault(item => item.Line == line) ?? ServiceLineTotals.Empty(line);

    /// <summary>True when the period produced nothing at all — what an empty-state message asks.</summary>
    public bool IsEmpty => Counts.Total == 0;

    private decimal ReceivedBy(PaymentMethod method)
        => Methods.Where(entry => entry.Method == method).Sum(entry => entry.Received);
}
