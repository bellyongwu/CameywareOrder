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
    public DateTime? LastModifiedDate { get; set; }
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
    // Free-text reason entered by the shop when the order is cancelled or returned
    // (取消理由 / 退货理由, same underlying field with a status-driven placeholder/label).
    public string? StatusReason { get; set; }
    // Stable key for the selected preset reason category (CustomerDoesNotWant /
    // ServiceUnsatisfactory / ProductIssue / PriceTooHigh / Other). Only meaningful when
    // Status is Cancelled/Returned. When the category is "Other", StatusReason holds the
    // free-text detail; for every other category StatusReason is unused/null.
    public string? StatusReasonCategory { get; set; }
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

    // Nominal deposits entered by the shop (pre-tax deposit amount), summed across sections.
    [NotMapped]
    public decimal TotalDownpayment
        => (AlterationDownpayment ?? 0m) + (CustomMadeDownpayment ?? 0m) + (ClothingDownpayment ?? 0m);

    // Per-section money split. Tax attaches to each portion (deposit / final balance)
    // ONLY when that portion is paid by card, so the nominal deposit and the
    // actually-received deposit can differ — e.g. a 300 deposit paid by card
    // is received as 339 at 13%, while the same deposit paid by cash is received as 300.
    [NotMapped]
    public SectionPayment AlterationMoney
        => CalculateSectionPayment(AlterationSubtotal ?? 0m, AlterationDownpayment ?? 0m, AlterationTaxRate ?? 0m,
            AlterationDownpaymentMethod, AlterationFinalBalanceMethod);

    [NotMapped]
    public SectionPayment ClothingMoney
        => CalculateSectionPayment(ClothingSubtotal ?? 0m, ClothingDownpayment ?? 0m, ClothingTaxRate ?? 0m,
            ClothingDownpaymentMethod, ClothingFinalBalanceMethod);

    // Custom-made pricing mirrors the other sections: the base charge is the sum of the
    // individual records' prices, and tax is applied per payment portion.
    [NotMapped]
    public decimal CustomMadeSubtotal => CustomMadeRecords.Sum(r => r.Subtotal);

    [NotMapped]
    public SectionPayment CustomMadeMoney
        => CalculateSectionPayment(CustomMadeSubtotal, CustomMadeDownpayment ?? 0m, CustomMadeTaxRate ?? 0m,
            CustomMadeDownpaymentMethod, CustomMadeFinalBalanceMethod);

    // Per-section totals (deposit charge + final-balance charge, each taxed by its own method).
    [NotMapped]
    public decimal AlterationTotal => AlterationMoney.Total;

    [NotMapped]
    public decimal ClothingTotal => ClothingMoney.Total;

    [NotMapped]
    public decimal CustomMadeTotal => CustomMadeMoney.Total;

    // Per-section tax amounts (charge minus subtotal). Used by the order detail
    // panel, which only shows a tax line when the amount is greater than zero.
    [NotMapped]
    public decimal AlterationTax => AlterationMoney.Tax;

    [NotMapped]
    public decimal ClothingTax => ClothingMoney.Tax;

    [NotMapped]
    public decimal CustomMadeTax => CustomMadeMoney.Tax;

    // Total tax collected across every charged section, shown as a single line on the receipt.
    [NotMapped]
    public decimal TotalTax => AlterationTax + ClothingTax + CustomMadeTax;

    // Actually-received deposits across sections (received deposit): each nominal deposit
    // plus its tax when that deposit was paid by card.
    [NotMapped]
    public decimal ReceivedDownpayment
        => AlterationMoney.ReceivedDownpayment + CustomMadeMoney.ReceivedDownpayment + ClothingMoney.ReceivedDownpayment;

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

    // True when the order carries at least one custom-made record that has captured
    // garment measurements. Drives the "定制服务" list flag and gates the measurement
    // print actions (measurement printing only makes sense when there are measurements).
    [NotMapped]
    public bool HasCustomMadeService
        => CustomMadeRecords.Any(record => record.Garments.Any(garment =>
            garment.Values.Any(value =>
                !string.IsNullOrWhiteSpace(value.Cm) || !string.IsNullOrWhiteSpace(value.In))));

    // A section is cleared when it carries no charge, has been explicitly marked
    // cleared, or its deposit already covers the full section total.
    [NotMapped]
    public bool AlterationSectionCleared
        => IsSectionCleared(AlterationMoney, AlterationBalanceCleared);

    [NotMapped]
    public bool CustomMadeSectionCleared
        => IsSectionCleared(CustomMadeMoney, CustomMadeBalanceCleared);

    [NotMapped]
    public bool ClothingSectionCleared
        => IsSectionCleared(ClothingMoney, ClothingBalanceCleared);

    // Outstanding final balance across sections: the taxed final charge on every section
    // that is not yet cleared.
    [NotMapped]
    public decimal FinalBalance
        => SectionResidual(AlterationMoney, AlterationBalanceCleared)
         + SectionResidual(CustomMadeMoney, CustomMadeBalanceCleared)
         + SectionResidual(ClothingMoney, ClothingBalanceCleared);

    // Actually-received final balance across cleared sections, including tax when that
    // final balance was paid by card.
    [NotMapped]
    public decimal ReceivedFinalBalance
        => SectionReceivedFinal(AlterationMoney, AlterationBalanceCleared)
         + SectionReceivedFinal(CustomMadeMoney, CustomMadeBalanceCleared)
         + SectionReceivedFinal(ClothingMoney, ClothingBalanceCleared);

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

    // An order is treated as picked up / completed once it has been shipped: shipping
    // hands the goods to the customer, so "Shipped" and "Completed" are equivalent for
    // pickup-related display (gray-out, balance-cleared label, etc.).
    [NotMapped]
    public bool IsPickedUp => Status is OrderStatus.Shipped or OrderStatus.Completed;

    // A cancelled or returned order is treated as refunded (fully or partially): the
    // remaining balance is no longer collectable, so it drives the 已退款或部分退款
    // balance status and the refund locking in the editor.
    [NotMapped]
    public bool IsRefunded => Status is OrderStatus.Cancelled or OrderStatus.Returned;

    // Single source of truth for the balance-status indicator used by the list, the
    // detail panel and the receipt (each maps this to its own label + colour).
    [NotMapped]
    public BalanceStatusKind PaymentStatusKind
    {
        get
        {
            if (IsRefunded)
                return BalanceStatusKind.Refunded;
            if (!IsBalanceCleared)
                return BalanceStatusKind.Outstanding;
            return IsPickedUp
                ? BalanceStatusKind.ClearedPickedUp
                : BalanceStatusKind.ClearedNotPickedUp;
        }
    }


    private static bool IsSectionCleared(SectionPayment money, bool balanceCleared)
        => money.Total <= 0m || balanceCleared || money.FinalBase <= 0m;

    private static decimal SectionResidual(SectionPayment money, bool balanceCleared)
        => (balanceCleared || money.FinalBase <= 0m) ? 0m : money.FinalCharge;

    private static decimal SectionReceivedFinal(SectionPayment money, bool balanceCleared)
        => (balanceCleared && money.FinalBase > 0m) ? money.FinalCharge : 0m;

    // Splits a service section into its deposit and final-balance money, applying tax to
    // each portion only when that portion is settled by card. The deposit is the pre-tax
    // amount entered by the shop and ReceivedDownpayment is that deposit after any card
    // tax. FinalBase is the pre-tax remainder and FinalCharge is that remainder after any
    // card tax. The deposit is capped at the subtotal so the final balance never goes below zero.
    public static SectionPayment CalculateSectionPayment(
        decimal subtotal, decimal deposit, decimal ratePercent,
        PaymentMethod? downpaymentMethod, PaymentMethod? finalBalanceMethod)
    {
        var safeSubtotal = subtotal < 0m ? 0m : subtotal;
        var safeDeposit = Math.Clamp(deposit, 0m, safeSubtotal);
        var finalBase = safeSubtotal - safeDeposit;
        var rate = ratePercent < 0m ? 0m : ratePercent;

        var depositRate = downpaymentMethod == PaymentMethod.Card ? rate : 0m;
        var finalRate = finalBalanceMethod == PaymentMethod.Card ? rate : 0m;

        var receivedDownpayment = safeDeposit + (safeDeposit * depositRate / 100m);
        var finalCharge = finalBase + (finalBase * finalRate / 100m);

        return new SectionPayment(
            safeSubtotal,
            safeDeposit,
            finalBase,
            receivedDownpayment,
            finalCharge,
            receivedDownpayment + finalCharge,
            (receivedDownpayment - safeDeposit) + (finalCharge - finalBase));
    }

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

// Immutable money split for one service section. See Order.CalculateSectionPayment.
public readonly record struct SectionPayment(
    decimal Subtotal,
    decimal Deposit,
    decimal FinalBase,
    decimal ReceivedDownpayment,
    decimal FinalCharge,
    decimal Total,
    decimal Tax);

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
    Processing = 1, // Processing
    Shipped = 2,    // Shipped
    Completed = 3,  // Completed (legacy Delivered)
    Cancelled = 4,  // Cancelled
    Returned = 5    // Returned
}

// The mutually-exclusive balance-status buckets shown to the shop. Each consumer maps
// this to its own localized label and colour (green / light green / orange / red).
public enum BalanceStatusKind
{
    Outstanding,        // 未结清
    ClearedPickedUp,    // 已结清（已取货）
    ClearedNotPickedUp, // 已结清（未取货）
    Refunded            // 已退款或部分退款
}
