using System.IO;
using System.Text.Json;
using CameywareOrder.Configuration;
using CameywareOrder.Data;
using CameywareOrder.Models;

namespace CameywareOrder.Services;

/// <summary>
/// The preset order history a demo store is created with: loads
/// <c>Settings/System/Defaults/demo-orders.json</c> and writes it into a shop as real orders.
/// </summary>
/// <remarks>
/// Shaped like <see cref="TaxJurisdictions"/> and <see cref="PhoneCountries"/> — a bounded shipped
/// set, loaded once, cached, and degrading to "nothing" rather than throwing, because a corrupt
/// presets file must cost the demo store its history and not the ability to create one.
///
/// Two things about the data are deliberate and easy to undo by accident:
///
/// <list type="bullet">
/// <item>Every day in the file is an OFFSET from the seeding day, never a date. A file of absolute
/// dates ages: a year after it ships, every demo order is long collected, the pickup queue is empty,
/// no row is overdue and the settlement report has nothing in its period. Resolving the offsets here
/// is what keeps a demo store looking like a shop that is trading today.</item>
/// <item>Twenty of the hundred orders fall inside the last seven days. The settlement report defaults
/// to month-to-date, so a set spread evenly backwards produces an empty report whenever the demo
/// store is created near the start of a month — which is exactly the shape this project shipped
/// once already.</item>
/// </list>
/// </remarks>
public static class DemoOrders
{
    /// <summary>
    /// The rate demo orders are taxed at where the shop's own location quotes none.
    /// </summary>
    /// <remarks>
    /// Canada and the US both ship with <c>standardRatePercent: 0</c> — sales tax is added at
    /// settlement there and the figure is the shop's to enter — so a demo store seeded straight from
    /// the preset shows zero tax on every order, zero tax in the settlement report and an empty tax
    /// line on every receipt. That demonstrates nothing. A demo store is not a real one, so it is
    /// given a plausible rate to show the arithmetic working; a located shop that quotes a real rate
    /// uses that instead.
    /// </remarks>
    public const decimal DemonstrationRatePercent = 13m;

    /// <summary>Hour of the local trading day the first seeded order is timed at.</summary>
    private const int TradingDayStartHour = 9;

    /// <summary>Minutes between consecutive seeded orders taken on the same day.</summary>
    private const int MinutesBetweenOrders = 15;

    /// <summary>How many distinct times of day the seeder cycles through before repeating.</summary>
    private const int TimeSlotsPerDay = 40;

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private static IReadOnlyList<DemoOrderTemplate>? _cached;

    /// <summary>Every preset order the build ships, in file order. Empty when the file is unreadable.</summary>
    public static IReadOnlyList<DemoOrderTemplate> All => _cached ??= Load();

    /// <summary>How many orders a demo store will be seeded with — what the button can promise.</summary>
    public static int Count => All.Count;

    /// <summary>
    /// The rate a demo store's orders are taxed at: its location's standard rate when that location
    /// quotes one, otherwise <see cref="DemonstrationRatePercent"/>.
    /// </summary>
    public static decimal TaxRatePercentFor(Shop shop)
    {
        var standard = TaxJurisdictions.For(shop).StandardRatePercent;
        return standard > 0m ? standard : DemonstrationRatePercent;
    }

    /// <summary>
    /// Writes the whole preset set into <paramref name="shop"/> as orders dated relative to
    /// <paramref name="today"/>, and returns how many were written.
    /// </summary>
    /// <remarks>
    /// <c>PaymentTaxRules.Active</c> is a process-wide value, assigned when a shop is OPENED, because
    /// every order the application handles belongs to the open shop. A demo store is seeded from
    /// Store Management, which can be reached with a different shop open or none at all — so every
    /// total computed here would be taxed by the wrong shop's rules. The demo shop's own rules are
    /// therefore made active for the length of the seed and restored afterwards. The alternative was
    /// a second copy of the tax arithmetic, and a copied money rule is free to disagree with the
    /// receipt that reprints the same order.
    ///
    /// Shop stamping is suppressed for the same reason <c>ShopArchive</c> suppresses it: the caller
    /// knows which shop these orders belong to and the OPEN one is not it.
    /// </remarks>
    public static int Seed(AppDbContext db, Shop shop, DateTime today)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(shop);

        var templates = All;
        if (templates.Count == 0)
            return 0;

        var previousRules = PaymentTaxRules.Active;
        PaymentTaxRules.SetActive(shop.PaymentTaxRules);

        try
        {
            var numbers = new HashSet<string>(StringComparer.Ordinal);
            var orders = new List<Order>(templates.Count);

            for (var index = 0; index < templates.Count; index++)
                orders.Add(BuildOrder(shop, templates[index], today, index, numbers));

            using (db.SuppressShopStamping())
            {
                db.Orders.AddRange(orders);
                db.SaveChanges();
            }

            return orders.Count;
        }
        finally
        {
            PaymentTaxRules.SetActive(previousRules);
        }
    }

    // ── building one order ────────────────────────────────────────────────────────────────────────

    private static Order BuildOrder(
        Shop shop, DemoOrderTemplate template, DateTime today, int index, ISet<string> numbers)
    {
        // Math.Abs so a file that writes the offset as a negative number still means "days ago"
        // rather than dating the order into the future, where the pickup colours would read as a bug.
        var takenLocal = today.Date
            .AddDays(-Math.Abs(template.OrderDaysAgo))
            .AddHours(TradingDayStartHour)
            .AddMinutes(index % TimeSlotsPerDay * MinutesBetweenOrders);

        var rate = TaxRatePercentFor(shop);

        var order = new Order
        {
            ShopId = shop.Id,
            OrderNumber = ComposeUniqueNumber(shop, takenLocal, index + 1, numbers),
            CustomerName = template.CustomerName ?? string.Empty,
            PhoneNumber = template.PhoneNumber ?? string.Empty,
            Email = template.Email,
            Address = template.Address,
            OrderDate = ToUtc(takenLocal),
            LastModifiedDate = ToUtc(takenLocal),
            ExpectedPickupDate = Order.ToStoredDate(takenLocal.Date.AddDays(template.PickupDaysAfterOrder)),
            CurrencyType = shop.CurrencyType,
            PricesIncludeTax = TaxJurisdictions.PricesIncludeTax(shop),
            ServiceType = ParseEnum(template.ServiceType, OrderServiceType.Alterations),
            Status = ParseEnum(template.Status, OrderStatus.Processing),
            StatusReasonCategory = template.StatusReasonCategory,
            StatusReason = template.StatusReason,
            AdditionalNotes = template.Notes,
        };

        ApplyAlteration(order, template.Alteration, rate);
        ApplyClothing(order, template, rate);
        ApplyCustomMade(order, template.CustomMade, rate);

        // The one figure that is STORED rather than derived, so it has to be computed the same way
        // the editor computes it on save — which is what the rules swap above is for.
        order.TotalAmount = order.ComputedSectionsTotal;

        return order;
    }

    private static void ApplyAlteration(Order order, DemoOrderSection? section, decimal rate)
    {
        if (section is null || section.Subtotal <= 0m)
            return;

        order.AlterationSubtotal = section.Subtotal;
        order.AlterationDownpayment = section.Deposit;
        order.AlterationDownpaymentMethod = ParseMethod(section.DepositMethod);
        order.AlterationDownpaymentCompleted = section.DepositReceived;
        order.AlterationFinalBalanceMethod = ParseMethod(section.FinalMethod);
        order.AlterationBalanceCleared = section.Cleared;
        order.AlterationTaxRate = rate;
        order.AlterationFinalTaxRate = rate;
    }

    private static void ApplyClothing(Order order, DemoOrderTemplate template, decimal rate)
    {
        var section = template.Clothing;
        if (section is null || section.Subtotal <= 0m)
            return;

        order.ClothingSubtotal = section.Subtotal;
        order.ClothingDownpayment = section.Deposit;
        order.ClothingDownpaymentMethod = ParseMethod(section.DepositMethod);
        order.ClothingDownpaymentCompleted = section.DepositReceived;
        order.ClothingFinalBalanceMethod = ParseMethod(section.FinalMethod);
        order.ClothingBalanceCleared = section.Cleared;
        order.ClothingTaxRate = rate;
        order.ClothingFinalTaxRate = rate;

        foreach (var item in template.Items ?? new List<DemoOrderItem>())
        {
            if (string.IsNullOrWhiteSpace(item.ProductId) || item.Quantity <= 0)
                continue;

            order.Items.Add(new OrderItem
            {
                ProductName = item.ProductId,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
            });
        }
    }

    private static void ApplyCustomMade(Order order, DemoCustomMadeSection? section, decimal rate)
    {
        if (section is null || section.Price <= 0m)
            return;

        order.CustomMadeDownpayment = section.Deposit;
        order.CustomMadeDownpaymentMethod = ParseMethod(section.DepositMethod);
        order.CustomMadeDownpaymentCompleted = section.DepositReceived;
        order.CustomMadeFinalBalanceMethod = ParseMethod(section.FinalMethod);
        order.CustomMadeBalanceCleared = section.Cleared;
        order.CustomMadeTaxRate = rate;
        order.CustomMadeFinalTaxRate = rate;

        // The section's charge comes from the RECORD, never from a stored subtotal — see
        // DemoCustomMadeSection. Serialised through the real type for the same reason.
        order.CustomMadeRecordsJson = JsonSerializer.Serialize(
            new List<CustomMadeServiceRecord> { BuildMeasurementRecord(order, section, rate) });
    }

    private static CustomMadeServiceRecord BuildMeasurementRecord(
        Order order, DemoCustomMadeSection section, decimal rate)
    {
        var record = new CustomMadeServiceRecord
        {
            CustomerName = order.CustomerName,
            PhoneNumber = order.PhoneNumber,
            Email = order.Email,
            Price = section.Price,
            TaxRate = rate,
        };

        if (string.IsNullOrWhiteSpace(section.GarmentId) || section.Measurements is not { Count: > 0 })
            return record;

        // Centimetres only. MeasurementUnits.Resolve converts from whichever unit was filled in, so
        // writing both would be inventing a second figure the shop never entered.
        record.Garments.Add(new GarmentMeasurement
        {
            GarmentId = section.GarmentId,
            Values = section.Measurements
                .Select(pair => new MeasurementValue { TermId = pair.Key, Cm = pair.Value })
                .ToList(),
        });

        return record;
    }

    /// <summary>
    /// One order number, stepped forward by a second until it is one this batch has not used.
    /// </summary>
    /// <remarks>
    /// The same collision rule <c>OrderNumberFormatter.ReserveTimestamp</c> applies, but against the
    /// BATCH rather than the database: a demo store is brand new, so nothing is taken in the database
    /// and everything is taken by the ninety-nine orders written beside this one. Reserve could not
    /// answer this — it asks the database, and EF cannot see rows that are added but not yet saved,
    /// which is the same trap batch Copy Order hit.
    /// </remarks>
    private static string ComposeUniqueNumber(Shop shop, DateTime moment, int sequence, ISet<string> taken)
    {
        for (var attempt = 0; attempt < 10_000; attempt++)
        {
            var candidate = OrderNumberFormatter.Compose(shop, moment, sequence);
            if (taken.Add(candidate))
                return candidate;

            moment = moment.AddSeconds(1);
        }

        return OrderNumberFormatter.Compose(shop, moment, sequence);
    }

    private static DateTime ToUtc(DateTime local)
        => DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime();

    private static T ParseEnum<T>(string? name, T fallback) where T : struct, Enum
        => Enum.TryParse<T>(name, ignoreCase: true, out var value) && Enum.IsDefined(value) ? value : fallback;

    /// <summary>A payment method name, or null when the file leaves the portion unsettled.</summary>
    private static PaymentMethod? ParseMethod(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        return Enum.TryParse<PaymentMethod>(name, ignoreCase: true, out var method) && Enum.IsDefined(method)
            ? method
            : null;
    }

    // ── loading ───────────────────────────────────────────────────────────────────────────────────

    private static IReadOnlyList<DemoOrderTemplate> Load()
    {
        try
        {
            var path = SystemSettingsPaths.DemoOrdersFile;
            if (!File.Exists(path))
                return Array.Empty<DemoOrderTemplate>();

            var payload = JsonSerializer.Deserialize<DemoOrdersPayload>(File.ReadAllText(path), ReadOptions);
            var entries = payload?.Orders;
            if (entries is null)
                return Array.Empty<DemoOrderTemplate>();

            // An entry with no customer is not an order anybody would recognise on the list, and the
            // column is NOT NULL — dropping it costs one demo record rather than the whole seed.
            return entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.CustomerName))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Array.Empty<DemoOrderTemplate>();
        }
    }

    private sealed record DemoOrdersPayload(int Version, List<DemoOrderTemplate>? Orders);
}
