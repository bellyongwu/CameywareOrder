using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using HotChocolate;

namespace CameywareOrder.Models;

public class Order
{
    public int Id { get; set; }

    /// <summary>
    /// Owning shop. Never set this by hand on a new order — <c>AppDbContext.SaveChangesAsync</c>
    /// stamps it from the active shop, because several creation paths (Copy Order, the GraphQL
    /// mutation) build an Order from an explicit property list and would otherwise drop it
    /// silently, leaving the order invisible in every view.
    ///
    /// Hidden from GraphQL: <c>Query.GetOrders</c> is decorated with [UseFiltering]/[UseSorting],
    /// which would otherwise publish shopId as a filterable field and advertise the existence of
    /// other shops. Results are already confined by the query filter regardless.
    /// </summary>
    [GraphQLIgnore]
    public int ShopId { get; set; }

    public string OrderNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Address { get; set; }
    public DateTime OrderDate { get; set; } = DateTime.UtcNow;
    public DateTime? LastModifiedDate { get; set; }

    /// <summary>
    /// The crew member who last saved this order, as their name READ AT THE TIME — for the record
    /// printed on a receipt.
    /// </summary>
    /// <remarks>
    /// The rendered name, not the login and not a foreign key. A receipt is a historical document:
    /// resolving the name when it is printed would change what an old receipt says the day somebody
    /// is renamed, and would leave it blank the day they are deleted. Accounts live in
    /// credentials.json, outside this database, so there is nothing to point a key at in any case.
    ///
    /// Null on every order saved before this column existed, and on anything written by the GraphQL
    /// API, which has no signed-in user. Callers omit the line rather than printing an empty one.
    /// </remarks>
    public string? LastModifiedBy { get; set; }
    public CurrencyType CurrencyType { get; set; } = CurrencyType.CAD;

    /// <summary>
    /// Whether the amounts on this order are quoted tax-inclusive. Frozen onto the order at save
    /// time from its shop's location (<c>TaxJurisdictions</c>), exactly as <see cref="CurrencyType"/>
    /// is, so a receipt reprinted after the shop moves or a rate changes still shows what was
    /// actually charged. False on every order saved before this column existed — they were all
    /// priced tax-exclusive — so their stored figures are unchanged.
    /// </summary>
    public bool PricesIncludeTax { get; set; }
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
    // (Order.Fields.CancelReason / .ReturnReason — the same field, with a status-driven label).
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
    // XxxTaxRate is the DEPOSIT-stage rate and XxxFinalTaxRate the FINAL-BALANCE-stage
    // rate: the shop can charge a different card rate on each portion. A null final rate
    // means the order predates the split, so the single stored rate applies to both.
    public decimal? AlterationSubtotal { get; set; }
    public decimal? AlterationTaxRate { get; set; }
    public decimal? AlterationFinalTaxRate { get; set; }
    public decimal? ClothingSubtotal { get; set; }
    public decimal? ClothingTaxRate { get; set; }
    public decimal? ClothingFinalTaxRate { get; set; }
    public decimal? CustomMadeTaxRate { get; set; }
    public decimal? CustomMadeFinalTaxRate { get; set; }

    /// <summary>
    /// How each section's stages were split across payment types, as JSON. Null on every order written
    /// before v4.0, which reads back as "no section is split" — the single-method arithmetic, unchanged.
    /// </summary>
    /// <remarks>
    /// One column for all three sections: see <see cref="OrderPaymentSplits"/>. Read through
    /// <see cref="PaymentSplits"/> rather than parsed at each use.
    /// </remarks>
    public string? PaymentSplitsJson { get; set; }

    public string? Notes { get; set; }
    public List<OrderItem> Items { get; set; } = new();

    /// <summary>
    /// The parsed splits. Parsed on every read, like <see cref="CustomMadeRecords"/> beside it —
    /// a cached copy on a tracked entity is a second source of truth for the same column, and this one
    /// feeds the money calculation.
    /// </summary>
    [NotMapped]
    public OrderPaymentSplits PaymentSplits => OrderPaymentSplits.FromJson(PaymentSplitsJson);

    /// <summary>Writes the splits back, storing null when no section is split so the column stays empty.</summary>
    public void SetPaymentSplits(OrderPaymentSplits? splits)
        => PaymentSplitsJson = splits is null || !splits.Sections.Values.Any(s => s.AnyEnabled)
            ? null
            : splits.ToJson();

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
        => CalculateSectionPayment(SectionInput(OrderPaymentSplits.AlterationKey,
            AlterationSubtotal ?? 0m, AlterationDownpayment ?? 0m, AlterationTaxRate ?? 0m,
            AlterationFinalTaxRate ?? AlterationTaxRate ?? 0m,
            AlterationDownpaymentMethod, AlterationFinalBalanceMethod));

    [NotMapped]
    public SectionPayment ClothingMoney
        => CalculateSectionPayment(SectionInput(OrderPaymentSplits.ClothingKey,
            ClothingSubtotal ?? 0m, ClothingDownpayment ?? 0m, ClothingTaxRate ?? 0m,
            ClothingFinalTaxRate ?? ClothingTaxRate ?? 0m,
            ClothingDownpaymentMethod, ClothingFinalBalanceMethod));

    // Custom-made pricing mirrors the other sections: the base charge is the sum of the
    // individual records' prices, and tax is applied per payment portion.
    [NotMapped]
    public decimal CustomMadeSubtotal => CustomMadeRecords.Sum(r => r.Subtotal);

    [NotMapped]
    public SectionPayment CustomMadeMoney
        => CalculateSectionPayment(SectionInput(OrderPaymentSplits.CustomMadeKey,
            CustomMadeSubtotal, CustomMadeDownpayment ?? 0m, CustomMadeTaxRate ?? 0m,
            CustomMadeFinalTaxRate ?? CustomMadeTaxRate ?? 0m,
            CustomMadeDownpaymentMethod, CustomMadeFinalBalanceMethod));

    /// <summary>
    /// Assembles one section's calculation input, attaching its split lines when that section's card
    /// has the toggle on.
    /// </summary>
    /// <remarks>
    /// The split is attached only where it can change anything: a section that is not split, and any
    /// order in a tax-INCLUSIVE market, passes null and takes the arithmetic the application has always
    /// had. Deciding that HERE rather than in the calculation keeps one answer to "is this section
    /// split" for the model, the editor and the receipt.
    /// </remarks>
    private SectionPaymentInput SectionInput(
        string sectionKey, decimal subtotal, decimal deposit, decimal depositRate, decimal finalRate,
        PaymentMethod? depositMethod, PaymentMethod? finalMethod)
    {
        var split = PricesIncludeTax ? null : PaymentSplits.For(sectionKey);

        // Per STAGE, not per section: a deposit taken one way and a balance split across three is an
        // ordinary thing for a customer to do, and each stage answers only for itself.
        return new SectionPaymentInput(subtotal, deposit, depositRate, finalRate,
            depositMethod, finalMethod, PricesIncludeTax)
        {
            DepositSplit = split?.IsEnabled(finalStage: false) == true ? split.Charged(finalStage: false) : null,
            FinalSplit = split?.IsEnabled(finalStage: true) == true ? split.Charged(finalStage: true) : null,
        };
    }

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

    /// <summary>
    /// The one rate this order's embedded tax was carved out at, for the line that states it
    /// ("Includes VAT (6%)"). Zero when nothing here is taxed, or on a tax-EXCLUSIVE order, where
    /// there is no single rate to quote in the first place.
    /// </summary>
    /// <remarks>
    /// Reading the FIRST non-zero section rate is exact rather than approximate, and only in this
    /// mode: an inclusive order takes its rate from the jurisdiction, so every section and both
    /// portions of each carry the same number by construction — see
    /// <c>TaxJurisdictions.IncludedTaxRatePercent</c> and <c>OrderEditWindow.ApplyStageTaxRates</c>.
    /// The rates are read from the ORDER, not from the shop, because they were frozen at save: a
    /// receipt reprinted after the government moves the rate must still quote the rate it charged.
    ///
    /// Guarded on <see cref="PricesIncludeTax"/> rather than left general, because in the exclusive
    /// mode the sections legitimately differ — a cash deposit at 0% beside a card balance at 13% —
    /// and "the first non-zero one" would then be a number no line of the order actually agrees with.
    /// </remarks>
    [NotMapped]
    public decimal IncludedTaxRatePercent
    {
        get
        {
            if (!PricesIncludeTax)
                return 0m;

            return new[] { AlterationTaxRate, CustomMadeTaxRate, ClothingTaxRate }
                .FirstOrDefault(rate => rate is > 0m) ?? 0m;
        }
    }

    // Actually-received deposits across sections (received deposit): each nominal deposit
    // plus its tax when that deposit was paid by card. A deposit only counts once the shop
    // has confirmed it with the section's "deposit received" tick — an amount typed into
    // the form is what is expected, not yet what is in hand.
    [NotMapped]
    public decimal ReceivedDownpayment
        => SectionReceivedDeposit(AlterationMoney, AlterationDownpaymentCompleted)
            + SectionReceivedDeposit(CustomMadeMoney, CustomMadeDownpaymentCompleted)
            + SectionReceivedDeposit(ClothingMoney, ClothingDownpaymentCompleted);

    private static decimal SectionReceivedDeposit(SectionPayment money, bool depositCompleted)
        => depositCompleted ? money.ReceivedDownpayment : 0m;

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
    // garment measurements. Drives the Order.Fields.CustomMadeFlag list flag and gates the measurement
    // print actions (measurement printing only makes sense when there are measurements).
    [NotMapped]
    public bool HasCustomMadeService
        => CustomMadeRecords.Exists(record => record.Garments.Exists(garment =>
            garment.Values.Exists(value =>
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
    // remaining balance is no longer collectable, so it drives the Payment.Status.Refunded
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

    /// <summary>
    /// Splits a service section into its deposit and final-balance money. Each portion carries its
    /// own rate, so the shop can charge e.g. 5% on a card deposit and 7% on the card final balance.
    /// The deposit is capped at the subtotal so the final balance never goes below zero.
    /// </summary>
    /// <param name="pricesIncludeTax">
    /// Which of the two pricing modes the amounts are quoted in — see <see cref="IncludedTaxPayment"/>
    /// and <see cref="AddedTaxPayment"/>. REQUIRED on purpose: it was briefly optional, defaulting to
    /// tax-exclusive, and the default turned "every unconverted call site fails to compile" into
    /// silence. A harness that recomputed its expectations through the shorter overload kept exclusive
    /// arithmetic while the window it measured had gone inclusive, and nothing failed to build. The
    /// whole point of this being the one calculation is that every caller gets the SAME answer; an
    /// optional argument guarantees that whoever forgets it gets a different one.
    /// </param>
    public static SectionPayment CalculateSectionPayment(in SectionPaymentInput input)
    {
        var safeSubtotal = input.Subtotal < 0m ? 0m : input.Subtotal;
        var safeDeposit = Math.Clamp(input.Deposit, 0m, safeSubtotal);
        var finalBase = safeSubtotal - safeDeposit;

        if (input.PricesIncludeTax)
            return IncludedTaxPayment(safeSubtotal, safeDeposit, finalBase, input.DepositRatePercent, input.FinalRatePercent);

        return AddedTaxPayment(safeSubtotal, safeDeposit, finalBase, input);
    }

    /// <summary>
    /// TAX-INCLUSIVE (VAT / consumption-tax markets such as China, Japan and the EU): the entered
    /// amount ALREADY contains the tax, so nothing is added — the received figures equal what was
    /// entered, the total is the subtotal, and the tax is what was embedded in them.
    /// </summary>
    /// <remarks>
    /// The shop's per-method rules are deliberately NOT consulted here. A value-added tax is a
    /// property of the SALE, not of how it was settled: a cash sale in Tokyo carries the same
    /// consumption tax as a card one, so letting a "cash is tax free" rule zero it would make one
    /// price yield two different taxes depending on the tender. The rate arrives from the shop's
    /// jurisdiction instead — see <c>TaxJurisdiction.StandardRatePercent</c> — and applies
    /// unconditionally.
    /// </remarks>
    private static SectionPayment IncludedTaxPayment(
        decimal subtotal, decimal deposit, decimal finalBase,
        decimal depositRatePercent, decimal finalRatePercent)
    {
        var depositTax = EmbeddedTax(deposit, depositRatePercent);
        var finalTax = EmbeddedTax(finalBase, finalRatePercent);

        return new SectionPayment(subtotal, deposit, finalBase, deposit, finalBase, subtotal,
            depositTax + finalTax)
        {
            DepositTax = depositTax,
            FinalTax = finalTax,
            PricesIncludeTax = true
        };
    }

    /// <summary>The tax already inside a quoted amount: amount − amount ÷ (1 + rate).</summary>
    private static decimal EmbeddedTax(decimal amount, decimal ratePercent)
        => ratePercent <= 0m ? 0m : amount - (amount * 100m / (100m + ratePercent));

    /// <summary>
    /// TAX-EXCLUSIVE (Canada and the US, and every order saved before the mode existed): the entered
    /// amount is pre-tax and tax is ADDED on top, per portion, only when the method that settled that
    /// portion is taxable under the shop's current rules (<see cref="PaymentTaxRules.Active"/> — by
    /// default the two card types, not cash or e-transfer).
    /// </summary>
    /// <remarks>
    /// The RATE comes from the order (what the shop actually charged and persisted); whether it
    /// applies at all comes from the shop's current rules. Keeping the stored rate is what makes a
    /// saved order print the same figures it was saved with, while the taxable/tax-free decision
    /// still follows the shop — a method the shop has since made tax free stops adding tax rather
    /// than silently keeping a rate nobody can see any more.
    ///
    /// A portion SPLIT across payment types is the same rule applied per line — see
    /// <see cref="PortionTax"/>. This is the only mode where a split changes anything, which is why it
    /// is offered nowhere else: where the price already contains the tax, how the money was tendered
    /// cannot move it.
    /// </remarks>
    private static SectionPayment AddedTaxPayment(
        decimal subtotal, decimal deposit, decimal finalBase, in SectionPaymentInput input)
    {
        var depositTax = PortionTax(deposit, input.DepositRatePercent, input.DepositMethod, input.DepositSplit);
        var finalTax = PortionTax(finalBase, input.FinalRatePercent, input.FinalMethod, input.FinalSplit);
        var receivedDownpayment = deposit + depositTax;
        var finalCharge = finalBase + finalTax;

        return new SectionPayment(subtotal, deposit, finalBase, receivedDownpayment, finalCharge,
            receivedDownpayment + finalCharge, depositTax + finalTax)
        {
            DepositTax = depositTax,
            FinalTax = finalTax,
            PricesIncludeTax = false
        };
    }

    /// <summary>
    /// The tax on one portion: the whole portion at its own rate, or — where the shop split it across
    /// payment types — each line at the rate ITS method carries.
    /// </summary>
    /// <remarks>
    /// A split of 400 cash + 200 card at 13% is taxed 26.00, not 78.00: the tax follows the tender,
    /// which is the entire point of the feature and the thing a single per-portion rate cannot express.
    ///
    /// The unsplit path is deliberately NOT a one-line split. It is reachable with no method chosen at
    /// all (a section still being filled in), and it charges on the portion's own base rather than on
    /// what the lines add up to — so an allocation that does not yet balance shows the tax on what is
    /// actually owed instead of on a half-typed number. The two paths agree exactly when one line
    /// covers the portion, which is what the harness pins down.
    ///
    /// <c>PaymentTaxRules.Active</c> is consulted per line, so a shop that makes credit cards tax free
    /// changes every order's split the same way it changes an unsplit one.
    /// </remarks>
    private static decimal PortionTax(
        decimal portionBase, decimal ratePercent, PaymentMethod? method, IReadOnlyList<PaymentSplitLine>? split)
    {
        var rules = PaymentTaxRules.Active;

        if (split is null || split.Count == 0)
        {
            var rate = rules.IsTaxable(method) && ratePercent > 0m ? ratePercent : 0m;
            return portionBase * rate / 100m;
        }

        return split
            .Where(line => line.Amount > 0m && rules.IsTaxable(line.Method) && line.RatePercent > 0m)
            .Sum(line => line.Amount * line.RatePercent / 100m);
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

/// <summary>
/// The currencies this build can name. WHICH of them a shop may offer is decided by the languages
/// installed on the system (see <c>ShopCurrencies</c>), not by this list — this is only the set of
/// values an order can be stored as.
/// </summary>
/// <remarks>
/// Persisted as INTEGERS on both Order and Shop, so the numbers are a compatibility surface: never
/// reorder or reuse one. Appending is safe; anything else re-denominates saved money.
///
/// A language file naming a currency that is not here has that entry dropped rather than guessed at,
/// so shipping a new language whose currency is missing costs one line — added here and to the
/// symbol table in <c>CurrencySettingService</c>, which the harness asserts is total over this enum.
/// </remarks>
public enum CurrencyType
{
    CAD = 1,
    USD = 2,
    CNY = 3,
    EUR = 4,
    JPY = 5
}

/// <summary>
/// Everything one section's money split is computed from. Passed as a struct rather than as loose
/// arguments so the split cannot be left off a call site.
/// </summary>
/// <remarks>
/// The parameter list had reached seven — the S107 limit — before the split was even a field, so this
/// is partly arithmetic. Mostly it is the lesson from the pricing-mode flag, which shipped as an
/// OPTIONAL argument and turned "every unconverted call site fails to compile" into silence: a harness
/// kept the shorter overload, kept the old arithmetic, and nothing failed to build while the numbers
/// stopped agreeing. A required struct makes the compiler enumerate the call sites again, and adding
/// the next input to it will do the same.
///
/// <see cref="DepositSplit"/> and <see cref="FinalSplit"/> are null for the unsplit case, which is
/// every order written before v4.0 and every section whose card has the toggle off.
/// </remarks>
public readonly record struct SectionPaymentInput(
    decimal Subtotal,
    decimal Deposit,
    decimal DepositRatePercent,
    decimal FinalRatePercent,
    PaymentMethod? DepositMethod,
    PaymentMethod? FinalMethod,
    bool PricesIncludeTax)
{
    /// <summary>The deposit stage's lines, or null when this section is not split.</summary>
    public IReadOnlyList<PaymentSplitLine>? DepositSplit { get; init; }

    /// <summary>The final stage's lines, or null when this section is not split.</summary>
    public IReadOnlyList<PaymentSplitLine>? FinalSplit { get; init; }
}

// Immutable money split for one service section. See Order.CalculateSectionPayment.
public readonly record struct SectionPayment(
    decimal Subtotal,
    decimal Deposit,
    decimal FinalBase,
    decimal ReceivedDownpayment,
    decimal FinalCharge,
    decimal Total,
    decimal Tax)
{
    /// <summary>
    /// The tax on the deposit portion, and on the final portion; together they are <see cref="Tax"/>.
    /// </summary>
    /// <remarks>
    /// Carried explicitly rather than left for each consumer to derive as
    /// <c>ReceivedDownpayment − Deposit</c>, which is the shape they all used until tax-inclusive
    /// pricing arrived: once the tax is embedded in the price those two differences are structurally
    /// ZERO while the section's tax is not, so the editor and the printed receipt both showed
    /// "tax 0" twice beside a non-zero total. A second pricing mode is not one branch in the money
    /// calculation — it is a branch in every place that EXPLAINS the number, which is why the split
    /// now travels with the money instead of being re-inferred downstream.
    ///
    /// Init properties rather than more constructor parameters, so the positional constructor stays
    /// at seven. <see cref="Order.CalculateSectionPayment"/> is the only thing that builds one, and
    /// it sets both on every path.
    /// </remarks>
    public decimal DepositTax { get; init; }

    /// <inheritdoc cref="DepositTax"/>
    public decimal FinalTax { get; init; }

    /// <summary>
    /// True when these amounts were quoted with the tax already inside them. Travels with the split
    /// so a consumer can present the figures correctly without reaching back to the order or the
    /// shop — the receipt converter has only the order, and a static formatting helper has neither.
    /// </summary>
    public bool PricesIncludeTax { get; init; }

    /// <summary>
    /// What the deposit stage adds up to: the section's subtotal plus the tax charged on the deposit
    /// — or just the subtotal when the tax is already inside it, because nothing is added on top.
    /// </summary>
    public decimal DepositStageTotal => PricesIncludeTax ? Subtotal : Subtotal + DepositTax;
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
    // Legacy single "card" value, from before debit and credit were charged separately. Kept so
    // orders already saved with it still resolve a name in every converter and on the receipt; the
    // editor shows one as Debit, which is what its old label ("Card (Visa/Debit)") actually named.
    // See PaymentTaxRules.Normalize.
    Card = 2,
    Cash = 3,
    None = 4,
    DebitCard = 5,
    CreditCard = 6
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
    Outstanding,        // Payment.Status.Outstanding
    ClearedPickedUp,    // Payment.Status.ClearedPickedUp
    ClearedNotPickedUp, // Payment.Status.ClearedNotPickedUp
    Refunded            // Payment.Status.Refunded
}
