using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace LeeYongeOrdering.Models;

public class Order
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Address { get; set; }
    public DateTime OrderDate { get; set; } = DateTime.UtcNow;
    public CurrencyType CurrencyType { get; set; } = CurrencyType.CAD;
    public OrderServiceType ServiceType { get; set; } = OrderServiceType.Alterations;
    public string? ServiceDetails { get; set; }
    public string? AdditionalNotes { get; set; }
    public decimal? Subtotal { get; set; }
    public decimal? TaxRate { get; set; }
    public string? ChestSize { get; set; }
    public string? JacketLength { get; set; }
    public string? CustomMadeRecordsJson { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Processing;
    public decimal TotalAmount { get; set; }
    public decimal? Downpayment { get; set; }
    public PaymentMethod? DownpaymentMethod { get; set; }
    public PaymentMethod? FinalBalanceMethod { get; set; }

    // Per-service payment tracking (Alterations)
    public decimal? AlterationDownpayment { get; set; }
    public PaymentMethod? AlterationDownpaymentMethod { get; set; }
    public bool AlterationDownpaymentCompleted { get; set; }
    public PaymentMethod? AlterationFinalBalanceMethod { get; set; }
    public bool AlterationBalanceCleared { get; set; }

    // Per-service payment tracking (Custom Made)
    public decimal? CustomMadeDownpayment { get; set; }
    public PaymentMethod? CustomMadeDownpaymentMethod { get; set; }
    public bool CustomMadeDownpaymentCompleted { get; set; }
    public PaymentMethod? CustomMadeFinalBalanceMethod { get; set; }
    public bool CustomMadeBalanceCleared { get; set; }

    // Per-service payment tracking (Clothing / Ready Made)
    public decimal? ClothingDownpayment { get; set; }
    public PaymentMethod? ClothingDownpaymentMethod { get; set; }
    public bool ClothingDownpaymentCompleted { get; set; }
    public PaymentMethod? ClothingFinalBalanceMethod { get; set; }
    public bool ClothingBalanceCleared { get; set; }

    // Per-service pricing (each service section is priced independently so its
    // charge and cleared status can be evaluated without touching the others).
    public decimal? AlterationSubtotal { get; set; }
    public decimal? AlterationTaxRate { get; set; }
    public decimal? ClothingSubtotal { get; set; }
    public decimal? ClothingTaxRate { get; set; }
    public decimal? CustomMadeTaxRate { get; set; }

    public string? Notes { get; set; }
    public List<OrderItem> Items { get; set; } = new();

    [NotMapped]
    public decimal TotalDownpayment
        => (AlterationDownpayment ?? 0m) + (CustomMadeDownpayment ?? 0m) + (ClothingDownpayment ?? 0m);

    // Per-section charges. The persisted tax rate is already 0 whenever card was not
    // used, so the section total is simply subtotal + subtotal * rate%.
    [NotMapped]
    public decimal AlterationTotal
        => (AlterationSubtotal ?? 0m) + ((AlterationSubtotal ?? 0m) * (AlterationTaxRate ?? 0m) / 100m);

    [NotMapped]
    public decimal ClothingTotal
        => (ClothingSubtotal ?? 0m) + ((ClothingSubtotal ?? 0m) * (ClothingTaxRate ?? 0m) / 100m);

    // Custom-made pricing mirrors the other sections: the base charge is the sum of the
    // individual records' prices, and tax is applied at the section level (0 unless a
    // card payment was used, which is enforced when the rate is persisted).
    [NotMapped]
    public decimal CustomMadeSubtotal => CustomMadeRecords.Sum(r => r.Subtotal);

    [NotMapped]
    public decimal CustomMadeTotal
        => CustomMadeSubtotal + (CustomMadeSubtotal * (CustomMadeTaxRate ?? 0m) / 100m);

    // Per-section tax amounts (charge minus subtotal). Used by the order detail
    // panel, which only shows a tax line when the amount is greater than zero.
    [NotMapped]
    public decimal AlterationTax => AlterationTotal - (AlterationSubtotal ?? 0m);

    [NotMapped]
    public decimal ClothingTax => ClothingTotal - (ClothingSubtotal ?? 0m);

    [NotMapped]
    public decimal CustomMadeTax => CustomMadeTotal - CustomMadeSubtotal;

    [NotMapped]
    public decimal ComputedSectionsTotal => AlterationTotal + CustomMadeTotal + ClothingTotal;

    // A service section counts as "added" (and therefore shown on the receipt / detail panel)
    // only when it carries a charge and a deposit method has been chosen. A zero total or an
    // unselected deposit means the service was not actually added.
    [NotMapped]
    public bool AlterationAddedToReceipt => AlterationTotal > 0m && AlterationDownpaymentMethod is not null;

    [NotMapped]
    public bool ClothingAddedToReceipt => ClothingTotal > 0m && ClothingDownpaymentMethod is not null;

    [NotMapped]
    public bool CustomMadeAddedToReceipt => CustomMadeTotal > 0m && CustomMadeDownpaymentMethod is not null;

    // A section is cleared when it carries no charge, has been explicitly marked
    // cleared, or its deposit already covers the full section total.
    [NotMapped]
    public bool AlterationSectionCleared
        => IsSectionCleared(AlterationTotal, AlterationDownpayment, AlterationBalanceCleared);

    [NotMapped]
    public bool CustomMadeSectionCleared
        => IsSectionCleared(CustomMadeTotal, CustomMadeDownpayment, CustomMadeBalanceCleared);

    [NotMapped]
    public bool ClothingSectionCleared
        => IsSectionCleared(ClothingTotal, ClothingDownpayment, ClothingBalanceCleared);

    [NotMapped]
    public decimal FinalBalance
        => SectionResidual(AlterationTotal, AlterationDownpayment, AlterationBalanceCleared)
         + SectionResidual(CustomMadeTotal, CustomMadeDownpayment, CustomMadeBalanceCleared)
         + SectionResidual(ClothingTotal, ClothingDownpayment, ClothingBalanceCleared);

    // The final-balance portion actually collected on every cleared section
    // (section total minus its deposit), accumulated across all services.
    [NotMapped]
    public decimal ReceivedFinalBalance
        => SectionReceivedFinal(AlterationTotal, AlterationDownpayment, AlterationBalanceCleared)
         + SectionReceivedFinal(CustomMadeTotal, CustomMadeDownpayment, CustomMadeBalanceCleared)
         + SectionReceivedFinal(ClothingTotal, ClothingDownpayment, ClothingBalanceCleared);

    [NotMapped]
    public bool IsBalanceCleared
    {
        get
        {
            // A brand-new/empty order (no charges anywhere) starts as outstanding.
            if (TotalAmount <= 0m)
                return false;

            // Legacy orders saved before per-section subtotals existed cannot compute
            // section totals; fall back to the aggregate deposit-vs-total rule.
            if (ComputedSectionsTotal <= 0m)
                return TotalAmount - TotalDownpayment <= 0m;

            // Cleared only when every charged section is settled; empty sections count as cleared.
            return AlterationSectionCleared && CustomMadeSectionCleared && ClothingSectionCleared;
        }
    }

    private static bool IsSectionCleared(decimal sectionTotal, decimal? downpayment, bool balanceCleared)
    {
        if (sectionTotal <= 0m)
            return true;
        if (balanceCleared)
            return true;
        return (downpayment ?? 0m) >= sectionTotal;
    }

    private static decimal SectionResidual(decimal sectionTotal, decimal? downpayment, bool balanceCleared)
        => balanceCleared ? 0m : sectionTotal - (downpayment ?? 0m);

    private static decimal SectionReceivedFinal(decimal sectionTotal, decimal? downpayment, bool balanceCleared)
        => balanceCleared ? Math.Max(0m, sectionTotal - (downpayment ?? 0m)) : 0m;

    [NotMapped]
    public List<CustomMadeServiceRecord> CustomMadeRecords
    {
        get
        {
            if (string.IsNullOrWhiteSpace(CustomMadeRecordsJson))
                return new List<CustomMadeServiceRecord>();

            try
            {
                return JsonSerializer.Deserialize<List<CustomMadeServiceRecord>>(CustomMadeRecordsJson) ?? new List<CustomMadeServiceRecord>();
            }
            catch
            {
                return new List<CustomMadeServiceRecord>();
            }
        }
    }
}

public enum CurrencyType
{
    CAD = 1,
    USD = 2,
    CNY = 3
}

public enum OrderServiceType
{
    Alterations = 1,
    CustomMade = 2,
    ReadyMade = 3
}

public enum PaymentMethod
{
    Etransfer = 1,
    Card = 2,
    Cash = 3,
    None = 4
}

public enum OrderStatus
{
    Processing = 1, // 处理中
    Shipped = 2,    // 已发货
    Completed = 3,  // 已完成 (legacy Delivered)
    Cancelled = 4,  // 已取消
    Returned = 5    // 已退货
}
