using System.Text.Json.Serialization;

namespace CameywareOrder.Models;

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }

    /// <summary>
    /// The owning order. EF's relationship fix-up populates this whenever the items are loaded, so it
    /// completes an <c>Order → Items → Order</c> cycle.
    /// </summary>
    /// <remarks>
    /// <see cref="JsonIgnoreAttribute"/> because that cycle is fatal to any serializer and carries no
    /// information: whatever holds this item already IS the order. Exporting a shop threw
    /// "a possible object cycle was detected" from <c>ShopArchive</c> for exactly this reason.
    ///
    /// Fixed HERE rather than with <c>ReferenceHandler.IgnoreCycles</c> at the one call site that hit
    /// it: the option would have written a null into the payload and left the next serializer to
    /// rediscover the same trap. <c>OrderId</c> already carries the relationship, and EF is unaffected —
    /// the attribute is read by System.Text.Json alone.
    /// </remarks>
    [JsonIgnore]
    public Order Order { get; set; } = null!;
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal? PromotionalPrice { get; set; }
    public decimal EffectiveUnitPrice => PromotionalPrice.HasValue && PromotionalPrice.Value > 0
        ? PromotionalPrice.Value
        : UnitPrice;
    public decimal TotalPrice => Quantity * EffectiveUnitPrice;
}
