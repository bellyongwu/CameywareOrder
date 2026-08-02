namespace CameywareOrder.Models;

/// <summary>
/// One preset order from the shipped demo set (<c>Settings/System/Defaults/demo-orders.json</c>),
/// exactly as the file describes it.
/// </summary>
/// <remarks>
/// Plain data with no dates on it. Every day in the file is an OFFSET
/// (<see cref="OrderDaysAgo"/>, <see cref="PickupDaysAfterOrder"/>) rather than a calendar date,
/// because a demo store created next year has to look like a shop that has been trading — a file of
/// absolute dates ages into a list of orders that were all collected long ago, and the settlement
/// report, the pickup queue and the overdue colours all go flat. The seeder resolves the offsets
/// against the day it runs, so the store is always current.
///
/// Every string is nullable and every section optional, because this is read from a file a person
/// can edit: a missing or malformed entry costs that one order, not the seeding.
/// </remarks>
public sealed class DemoOrderTemplate
{
    public string? CustomerName { get; set; }

    public string? PhoneNumber { get; set; }

    public string? Email { get; set; }

    public string? Address { get; set; }

    /// <summary>An <see cref="OrderStatus"/> name. Anything unrecognised falls back to Processing.</summary>
    public string? Status { get; set; }

    /// <summary>An <see cref="OrderServiceType"/> name — the service the order is filed under.</summary>
    public string? ServiceType { get; set; }

    /// <summary>Days before the seeding day this order was taken.</summary>
    public int OrderDaysAgo { get; set; }

    /// <summary>Days after it was taken that the customer agreed to collect.</summary>
    public int PickupDaysAfterOrder { get; set; }

    public string? Notes { get; set; }

    /// <summary>A <c>StatusReasonCategory</c> key, present only on a cancelled or returned order.</summary>
    public string? StatusReasonCategory { get; set; }

    public string? StatusReason { get; set; }

    public DemoOrderSection? Alteration { get; set; }

    public DemoOrderSection? Clothing { get; set; }

    public DemoCustomMadeSection? CustomMade { get; set; }

    /// <summary>The ready-made lines, present when <see cref="Clothing"/> is.</summary>
    public List<DemoOrderItem>? Items { get; set; }
}

/// <summary>One priced service section of a preset order.</summary>
public sealed class DemoOrderSection
{
    public decimal Subtotal { get; set; }

    public decimal Deposit { get; set; }

    /// <summary>A <see cref="PaymentMethod"/> name settling the deposit.</summary>
    public string? DepositMethod { get; set; }

    /// <summary>A <see cref="PaymentMethod"/> name settling the final balance.</summary>
    public string? FinalMethod { get; set; }

    public bool DepositReceived { get; set; }

    public bool Cleared { get; set; }
}

/// <summary>
/// The custom-made section, which is priced differently from the other two: its charge comes from
/// the measurement RECORD's price, not from a stored subtotal.
/// </summary>
/// <remarks>
/// <c>CustomMadeServiceRecord.Subtotal</c> is computed from <c>Price</c>, so a hand-written JSON
/// record carrying a "subtotal" deserialises into a record worth nothing. The file therefore names
/// the field <c>price</c> and the seeder builds a real <c>CustomMadeServiceRecord</c> from it.
/// </remarks>
public sealed class DemoCustomMadeSection
{
    public decimal Price { get; set; }

    public decimal Deposit { get; set; }

    public string? DepositMethod { get; set; }

    public string? FinalMethod { get; set; }

    public bool DepositReceived { get; set; }

    public bool Cleared { get; set; }

    /// <summary>A predefined garment id (see <c>MeasurementTermDefaults.PredefinedGarmentIds</c>).</summary>
    public string? GarmentId { get; set; }

    /// <summary>Term id to centimetre value, for the garment above.</summary>
    public Dictionary<string, string>? Measurements { get; set; }
}

/// <summary>One ready-made line of a preset order.</summary>
public sealed class DemoOrderItem
{
    /// <summary>
    /// A product-catalogue id. These are a compatibility surface — see
    /// <c>ProductCatalogDefaults</c> — so the file names the shipped ones rather than free text.
    /// </summary>
    public string? ProductId { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }
}
