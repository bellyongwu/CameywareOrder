using CameywareOrder.Data;
using CameywareOrder.Models;

namespace CameywareOrder.Services;

/// <summary>
/// Builds a shop's order / receipt numbers from its configured format
/// (<see cref="Shop.OrderNumberMode"/> and friends).
///
/// The running counter is only ever advanced by <see cref="CommitSequence"/>, after an order has
/// actually been written: a number shown in a form that is then abandoned must not burn a receipt
/// number, because a gap in a receipt run is exactly what a tax audit asks about.
/// </summary>
public static class OrderNumberFormatter
{
    /// <summary>Used when a shop has not set a prefix of its own — the format the app always produced.</summary>
    public const string DefaultPrefix = "ORD";

    private const int MinPadding = 1;
    private const int MaxPadding = 10;

    /// <summary>
    /// The period the running counter belongs to. When the period rolls over, the counter restarts
    /// at 1 — which is what makes daily and yearly numbering reset with no scheduled job to run.
    /// A continuous run has no period, so its key never changes.
    /// </summary>
    public static string SequenceKeyFor(OrderNumberMode mode, DateTime now) => mode switch
    {
        OrderNumberMode.DailySequential => now.ToString("yyyyMMdd"),
        OrderNumberMode.YearlySequential => now.ToString("yyyy"),
        _ => string.Empty
    };

    /// <summary>The number this shop would give an order right now, without reserving it.</summary>
    public static string Preview(Shop shop, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(shop);
        return Compose(shop, now, ResolveNextSequence(shop, now));
    }

    /// <summary>
    /// The next free number for this shop: the configured format, advanced past anything already
    /// taken. The scan matters because order numbers can be typed by hand and databases get
    /// imported, so a stored counter alone is not proof a number is free.
    /// </summary>
    public static string Reserve(AppDbContext db, Shop shop, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(shop);

        if (shop.OrderNumberMode == OrderNumberMode.Timestamp)
            return Compose(shop, now, sequence: 0);

        var sequence = ResolveNextSequence(shop, now);
        var candidate = Compose(shop, now, sequence);

        // Bounded so a corrupt counter cannot spin: 10,000 collisions in a row means the format
        // itself is producing duplicates, and the caller's own uniqueness check is the backstop.
        for (var attempt = 0; attempt < 10_000 && IsTaken(db, candidate); attempt++)
        {
            sequence++;
            candidate = Compose(shop, now, sequence);
        }

        return candidate;
    }

    /// <summary>
    /// Records that <paramref name="orderNumber"/> has been used, so the next order starts after
    /// it. Call only once the order is saved. A number the shop typed by hand that does not match
    /// the configured format leaves the counter alone — it was never drawn from the run.
    /// </summary>
    public static void CommitSequence(Shop shop, string orderNumber, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(shop);

        if (shop.OrderNumberMode == OrderNumberMode.Timestamp)
            return;

        var used = ExtractSequence(shop, orderNumber, now);
        if (used is null)
            return;

        shop.OrderNumberSequenceKey = SequenceKeyFor(shop.OrderNumberMode, now);
        shop.OrderNumberNextSequence = used.Value + 1;
    }

    /// <summary>Assembles one number. Timestamp mode ignores <paramref name="sequence"/> entirely.</summary>
    public static string Compose(Shop shop, DateTime now, int sequence)
    {
        ArgumentNullException.ThrowIfNull(shop);

        var prefix = ResolvePrefix(shop);
        var running = sequence.ToString(new string('0', ResolvePadding(shop)));

        return shop.OrderNumberMode switch
        {
            OrderNumberMode.Sequential => Join(prefix, running),
            OrderNumberMode.DailySequential => Join(prefix, now.ToString("yyyyMMdd"), running),
            OrderNumberMode.YearlySequential => Join(prefix, now.ToString("yyyy"), running),
            _ => Join(prefix, now.ToString("yyyyMMdd"), now.ToString("HHmmss"))
        };
    }

    public static string ResolvePrefix(Shop shop)
    {
        ArgumentNullException.ThrowIfNull(shop);
        return string.IsNullOrWhiteSpace(shop.OrderNumberPrefix)
            ? string.Empty
            : shop.OrderNumberPrefix.Trim();
    }

    public static int ResolvePadding(Shop shop)
    {
        ArgumentNullException.ThrowIfNull(shop);
        return Math.Clamp(shop.OrderNumberPadding, MinPadding, MaxPadding);
    }

    /// <summary>
    /// The counter to use right now: the stored one while it still belongs to the current period,
    /// otherwise a fresh run at 1.
    /// </summary>
    private static int ResolveNextSequence(Shop shop, DateTime now)
    {
        var stored = shop.OrderNumberNextSequence < 1 ? 1 : shop.OrderNumberNextSequence;
        var currentKey = SequenceKeyFor(shop.OrderNumberMode, now);

        // Only the period-based modes can roll over. A continuous run has no period, so it must
        // never restart whatever is stored against it — restarting would re-issue receipt numbers
        // that have already been handed to customers, which is precisely what an audit looks for.
        if (currentKey.Length == 0)
            return stored;

        var storedKey = shop.OrderNumberSequenceKey ?? string.Empty;
        return string.Equals(currentKey, storedKey, StringComparison.Ordinal) ? stored : 1;
    }

    /// <summary>
    /// Reads the running number back out of a formatted order number, or null when it was not
    /// produced by this shop's current format (a hand-typed number, or one from before the format
    /// was changed).
    /// </summary>
    private static int? ExtractSequence(Shop shop, string orderNumber, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(orderNumber))
            return null;

        var expectedLead = Compose(shop, now, sequence: 0);
        var runningLength = ResolvePadding(shop);

        // The lead is everything the format contributes before the running number, so an order
        // number belongs to this run only when it matches that lead exactly.
        var lead = expectedLead[..^runningLength];
        if (!orderNumber.StartsWith(lead, StringComparison.Ordinal))
            return null;

        var tail = orderNumber[lead.Length..];
        return int.TryParse(tail, out var value) ? value : null;
    }

    private static bool IsTaken(AppDbContext db, string orderNumber)
        => db.Orders.Any(order => order.OrderNumber == orderNumber);

    /// <summary>Joins the parts with "-", skipping any that are empty so a blank prefix leaves no stray dash.</summary>
    private static string Join(params string[] parts)
        => string.Join("-", parts.Where(part => !string.IsNullOrEmpty(part)));
}
