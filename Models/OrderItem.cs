namespace LeeYongeOrdering.Models;

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
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
