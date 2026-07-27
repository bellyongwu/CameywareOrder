using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace CameywareOrder.Models;

/// <summary>
/// One shop (branch) whose orders and settings this installation manages. Every order belongs to
/// exactly one shop through <see cref="Order.ShopId"/>; the whole app works against a single
/// active shop at a time.
/// </summary>
public class Shop
{
    public int Id { get; set; }

    /// <summary>
    /// Stable identity that survives a database import. <see cref="Id"/> is a local autoincrement
    /// value, so two installations will happily allocate the same one — and the database can be
    /// carried between machines wholesale (see <c>GlobalSettingsPackage</c> and
    /// <c>DatabasePathProvider.ImportDatabaseFrom</c>). Anything stored OUTSIDE the database that
    /// belongs to a shop — its measurement terms file, its branding folder — must be keyed on this,
    /// never on <see cref="Id"/>, or an import silently hands one shop another shop's settings.
    /// </summary>
    public Guid PublicId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Short slug for the shop (e.g. "SH"), reserved for disambiguating order numbers between
    /// branches. Order numbers are currently timestamp-based and are only unique per shop by
    /// luck; this is the hook for fixing that without another schema change.
    /// </summary>
    public string? Code { get; set; }

    /// <summary>
    /// Language code to display name, serialized. The shop name is user-facing text and this app
    /// is bilingual, so a single string would force one language's users to read the other's name
    /// on screen and on printed receipts. Mirrors the per-language <c>Names</c> dictionary already
    /// used by <see cref="MeasurementTerm"/> and <see cref="GarmentType"/>.
    /// </summary>
    public string NamesJson { get; set; } = "{}";

    /// <summary>Language applied when this shop is opened. Null falls back to the global preference.</summary>
    public string? PreferredLanguageCode { get; set; }

    public CurrencyType CurrencyType { get; set; } = CurrencyType.CAD;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Hidden from the shop picker without deleting its orders.</summary>
    public bool IsArchived { get; set; }

    /// <summary>
    /// This shop's tax rule per payment method, serialized. Null means "never configured", which
    /// reads back as <see cref="PaymentTaxRules.CreateDefault"/> — cash and e-transfer tax free,
    /// both card types at 13% — so an existing shop keeps behaving exactly as it always did.
    /// </summary>
    public string? PaymentTaxRulesJson { get; set; }

    /// <summary>How this shop numbers its orders / receipts.</summary>
    public OrderNumberMode OrderNumberMode { get; set; } = OrderNumberMode.Timestamp;

    /// <summary>Leading text of an order number, e.g. "ORD" in ORD-000123. Blank means no prefix.</summary>
    public string? OrderNumberPrefix { get; set; }

    /// <summary>Digits the running number is padded to, so 12 prints as 0012 at a padding of 4.</summary>
    public int OrderNumberPadding { get; set; } = 4;

    /// <summary>
    /// The running number the next order will take. Advanced only after an order is actually
    /// saved, so abandoning a half-filled form does not burn a receipt number.
    /// </summary>
    public int OrderNumberNextSequence { get; set; } = 1;

    /// <summary>
    /// The period <see cref="OrderNumberNextSequence"/> belongs to (a date or a year, depending on
    /// the mode). When the current period no longer matches this, the counter restarts at 1 —
    /// which is what makes daily and yearly numbering reset without a scheduled job.
    /// </summary>
    public string? OrderNumberSequenceKey { get; set; }

    /// <summary>Decoded <see cref="PaymentTaxRulesJson"/>. Computed, never stored directly.</summary>
    [NotMapped]
    public PaymentTaxRules PaymentTaxRules => PaymentTaxRules.FromJson(PaymentTaxRulesJson);

    public void SetPaymentTaxRules(PaymentTaxRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        PaymentTaxRulesJson = rules.ToJson();
    }

    /// <summary>Language code to display name, decoded from <see cref="NamesJson"/>.</summary>
    [NotMapped]
    public Dictionary<string, string> Names
    {
        get
        {
            if (string.IsNullOrWhiteSpace(NamesJson))
                return new Dictionary<string, string>();

            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(NamesJson)
                    ?? new Dictionary<string, string>();
            }
            catch (JsonException)
            {
                return new Dictionary<string, string>();
            }
        }
    }

    public void SetNames(IReadOnlyDictionary<string, string> names)
        => NamesJson = JsonSerializer.Serialize(names);

    /// <summary>
    /// Display name in the requested language, falling back to any other language that has one and
    /// finally to an empty string, so a shop is never nameless on screen.
    /// </summary>
    public string ResolveName(string languageCode)
    {
        var names = Names;

        if (names.TryGetValue(languageCode, out var exact) && !string.IsNullOrWhiteSpace(exact))
            return exact;

        return names.Values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }
}

/// <summary>
/// How a shop composes its order / receipt numbers. <see cref="Timestamp"/> is the format the app
/// has always produced and stays the default, so no existing shop's numbering changes until it
/// picks another one.
/// </summary>
public enum OrderNumberMode
{
    /// <summary>PREFIX-20260727-153012 — unique by the second, no counter to keep.</summary>
    Timestamp = 0,

    /// <summary>PREFIX-000123 — one continuous run of numbers.</summary>
    Sequential = 1,

    /// <summary>PREFIX-20260727-0001 — a fresh run of numbers each day.</summary>
    DailySequential = 2,

    /// <summary>PREFIX-2026-0001 — a fresh run of numbers each year.</summary>
    YearlySequential = 3
}
