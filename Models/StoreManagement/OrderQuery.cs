namespace CameywareOrder.Models;

/// <summary>Which part of an order a search looks in.</summary>
/// <remarks>
/// The names are a compatibility surface only as far as the string table — each value has a
/// <c>Search.Field.&lt;name&gt;</c> key — so adding one costs a value, a case and five translations.
/// </remarks>
public enum OrderSearchField
{
    /// <summary>Everything below at once. The default, and what a shop means by "find".</summary>
    All,

    /// <summary>The receipt number, which is the one thing a customer arrives holding.</summary>
    OrderNumber,

    Customer,

    Phone,

    /// <summary>Free text: the service description and the notes.</summary>
    Notes
}

/// <summary>
/// What the shop is currently looking for: some text in some field, optionally narrowed to a status
/// and a period.
/// </summary>
/// <remarks>
/// ONE definition of "which orders are we talking about", used by three things that must never
/// disagree: the list on screen, the CSV the shop exports, and any count reported beside them. The
/// export in particular has to be exactly what the list shows — a spreadsheet that quietly contains
/// more rows than the screen it was taken from is worse than no export at all, because nobody
/// re-checks a file that looks right.
///
/// Immutable, with <c>with</c>-style replacement rather than settable properties, so a view model
/// cannot leave it half-updated between two rebuilds of the list.
///
/// Matching runs in memory rather than as a database query on purpose: the list already holds the
/// shop's whole order set (see <c>MainViewModel.LoadOrdersAsync</c>), and pushing the text match into
/// SQLite would make the search case-sensitivity depend on the provider's collation rather than on
/// <see cref="StringComparison.OrdinalIgnoreCase"/>, which is what the rest of this application means
/// by "contains".
/// </remarks>
public sealed record OrderQuery
{
    /// <summary>An empty query — everything the shop has.</summary>
    public static OrderQuery Empty { get; } = new();

    /// <summary>What to look for. Trimmed on use; blank means "do not filter by text".</summary>
    public string? Text { get; init; }

    /// <summary>Where to look for it.</summary>
    public OrderSearchField Field { get; init; } = OrderSearchField.All;

    /// <summary>Only orders in this status, or null for any.</summary>
    public OrderStatus? Status { get; init; }

    /// <summary>Only orders TAKEN in this period, or null for any.</summary>
    /// <remarks>
    /// The order date, not the pickup date. "Show me March" from a shop means the work they took in
    /// March — which is also the figure the settlement report is built on, so the two screens answer
    /// the same question when asked the same period.
    /// </remarks>
    public DateRange? Period { get; init; }

    /// <summary>Whether this narrows anything at all.</summary>
    public bool IsEmpty
        => string.IsNullOrWhiteSpace(Text) && Status is null && Period is null;

    /// <summary>Whether one order matches.</summary>
    public bool Matches(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (Status is { } status && order.Status != status)
            return false;

        if (Period is { } period && !period.Contains(order.OrderDate))
            return false;

        var keyword = Text?.Trim();
        return string.IsNullOrEmpty(keyword) || MatchesText(order, keyword);
    }

    /// <summary>The orders that match, in the order they were given.</summary>
    public List<Order> Apply(IEnumerable<Order> orders)
    {
        ArgumentNullException.ThrowIfNull(orders);
        return IsEmpty ? orders.ToList() : orders.Where(Matches).ToList();
    }

    private bool MatchesText(Order order, string keyword) => Field switch
    {
        OrderSearchField.OrderNumber => Has(order.OrderNumber, keyword),
        OrderSearchField.Customer => Has(order.CustomerName, keyword),
        OrderSearchField.Phone => Has(order.PhoneNumber, keyword),
        OrderSearchField.Notes => Has(order.ServiceDetails, keyword) || Has(order.AdditionalNotes, keyword),
        // "All" is every field the narrower options offer plus the two contact details, so choosing a
        // field can only ever narrow the result — never surface a row the default would have missed.
        _ => Has(order.OrderNumber, keyword)
             || Has(order.CustomerName, keyword)
             || Has(order.PhoneNumber, keyword)
             || Has(order.Email, keyword)
             || Has(order.Address, keyword)
             || Has(order.ServiceDetails, keyword)
             || Has(order.AdditionalNotes, keyword)
    };

    private static bool Has(string? value, string keyword)
        => !string.IsNullOrEmpty(value) && value.Contains(keyword, StringComparison.OrdinalIgnoreCase);
}
