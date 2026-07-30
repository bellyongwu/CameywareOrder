using System.Text.Json;
using System.Text.Json.Serialization;

namespace CameywareOrder.Models;

/// <summary>
/// One payment inside a stage: an amount, the method it was taken by, and the rate that method
/// carried when the order was saved.
/// </summary>
/// <remarks>
/// The RATE is frozen on the line for the same reason the section rates already are — a receipt
/// reprinted after the shop re-rates a card must still show what was charged. Whether the line is
/// taxed AT ALL still follows the shop's current rules, exactly as the unsplit path does: a method the
/// shop has since made tax free stops adding tax rather than keeping a rate nobody can see any more.
/// </remarks>
public sealed class PaymentSplitLine
{
    [JsonPropertyName("m")]
    public PaymentMethod Method { get; set; }

    [JsonPropertyName("a")]
    public decimal Amount { get; set; }

    [JsonPropertyName("r")]
    public decimal RatePercent { get; set; }
}

/// <summary>
/// How one service section's two stages were paid when the shop split them across payment types.
/// </summary>
/// <remarks>
/// <see cref="Enabled"/> is the section's own choice, covering BOTH stages: the toggle lives on the
/// payment card, and a card that splits its deposit across cash and a card almost always splits its
/// balance too. A section with it off keeps the single-method shape the application has always had, and
/// its line lists are not read at all.
///
/// Stored rather than derived from "are there lines", because a shop can turn the split on and type
/// nothing yet — and a half-filled split that silently reverted to the unsplit rule would charge a
/// different tax than the screen showed.
/// </remarks>
public sealed class SectionPaymentSplit
{
    public bool Enabled { get; set; }

    public List<PaymentSplitLine> Deposit { get; set; } = new();

    public List<PaymentSplitLine> Final { get; set; } = new();

    /// <summary>The lines for one stage, ignoring any that carry no money.</summary>
    /// <remarks>
    /// Zero-amount lines are kept in storage — a row the user cleared should not re-appear filled —
    /// but they are never charged, and a line with no amount must not contribute a tax of zero to a
    /// breakdown that then lists a payment method nobody paid with.
    /// </remarks>
    public IReadOnlyList<PaymentSplitLine> Charged(bool finalStage)
        => (finalStage ? Final : Deposit).Where(line => line.Amount > 0m).ToList();
}

/// <summary>
/// Every section's split, as stored on <c>Order.PaymentSplitsJson</c>.
/// </summary>
/// <remarks>
/// ONE column holding all three sections rather than six columns, because these are read and written
/// together on one screen and nothing queries them. It follows <c>CustomMadeRecordsJson</c>, which
/// solved the same problem: a shape that is a LIST per section does not decompose into scalar columns
/// without inventing a table nobody joins to.
///
/// Every order written before v4.0 has this null, which reads back as "no section splits" — the
/// unsplit arithmetic, unchanged, for the whole installed base.
/// </remarks>
public sealed class OrderPaymentSplits
{
    public const string AlterationKey = "Alteration";
    public const string CustomMadeKey = "CustomMade";
    public const string ClothingKey = "Clothing";

    /// <summary>Split per section, keyed by the section names above.</summary>
    public Dictionary<string, SectionPaymentSplit> Sections { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The section's split, created on demand so a caller never has to null-check.</summary>
    public SectionPaymentSplit For(string sectionKey)
    {
        if (!Sections.TryGetValue(sectionKey, out var split))
        {
            split = new SectionPaymentSplit();
            Sections[sectionKey] = split;
        }

        return split;
    }

    public string ToJson() => JsonSerializer.Serialize(this);

    /// <summary>Reads stored splits, falling back to none for null/blank/corrupt JSON.</summary>
    /// <remarks>
    /// Corrupt JSON degrades to the UNSPLIT arithmetic rather than throwing, which is the same choice
    /// the payment-tax rules make. It is the safe direction: an order whose split cannot be read shows
    /// and charges its single stored method, which is what every order did before this existed.
    /// </remarks>
    public static OrderPaymentSplits FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new OrderPaymentSplits();

        try
        {
            return JsonSerializer.Deserialize<OrderPaymentSplits>(json) ?? new OrderPaymentSplits();
        }
        catch (JsonException)
        {
            return new OrderPaymentSplits();
        }
    }
}
