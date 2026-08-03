using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using CameywareOrder.Data;
using CameywareOrder.Localization;
using CameywareOrder.Models;
using CameywareOrder.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.CodeAnalysis;

namespace CameywareOrder.Views;

public partial class OrderEditWindow : Window
{
    private const decimal DefaultTaxRate = 13m;
    private const string EditTitleKey = "OrderEdit.EditTitle";
    private const string ViewTitleKey = "OrderEdit.ViewTitle";
    private const string DownpaymentMethodKey = "OrderEdit.DownpaymentMethod";
    private const string FinalBalanceMethodKey = "OrderEdit.FinalBalanceMethod";
    private const string ValidationTitleKey = "OrderEdit.ValidationTitle";

    /// <summary>
    /// What is currently wrong with the form, in the order it was found. Filled by the checks, read by
    /// the banner and by the single dialog, cleared at the start of every pass.
    /// </summary>
    private readonly List<string> _validationProblems = new();
    // Stored in Order.ServiceDetails like the other alteration categories; marks the whole
    // alteration service as not part of this order. It is the first item in the picker and so
    // the default for a NEW order.
    private const string NoAlterationServiceTag = "None";
    // Fallback for a SAVED order whose stored category matches no item — never "None", or a
    // legacy free-text value would switch a charged alteration service off.
    private const string DefaultSavedAlterationCategoryTag = "GarmentAdjustments";
    // Status-reason category that requires the free-text detail to be filled in, and the
    // fallback for legacy records saved before the preset picker existed.
    private const string OtherStatusReasonTag = "Other";
    // Backstop against pathological backtracking on pasted input (S6444). MUST be declared before
    // the patterns that use it: static field initializers run in textual order, so a timeout
    // declared below them would still be TimeSpan.Zero when they construct — which Regex rejects,
    // and the failure would surface as a TypeInitializationException on first use, not a build error.
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex DecimalInputPattern =
        new("^\\d*(\\.\\d{0,2})?$", RegexOptions.None, RegexTimeout);
    // Muted grey used by the small-print breakdown lines; matches the TaxBreakdownLine XAML
    // style. Frozen and shared so the per-line TextBlocks don't each allocate a brush.
    // System.Windows.Media is fully qualified here: under ImplicitUsings, QuestPDF and
    // HotChocolate also define Color, so an unqualified name is ambiguous.
    private static readonly System.Windows.Media.SolidColorBrush BreakdownLineBrush =
        CreateFrozenBrush(0x7A, 0x86, 0x98);
    // Amber, for a service that carries items but no charge — a flag, not an error.
    private static readonly System.Windows.Media.SolidColorBrush UnpricedLineBrush =
        CreateFrozenBrush(0xC1, 0x7A, 0x0B);
    // The ready-made categories used to be a fixed list here, so every shop in every installation
    // sold the same five things and adding a sixth meant a rebuild. They now come from the SHOP's
    // own catalogue (ProductCatalogService); the ids live on in ProductCatalogDefaults, which is
    // what keeps orders saved under the old list resolving to the same names.

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LocalizationService _localization;
    private readonly Order? _existing;
    private readonly bool _isReadOnly;

    /// <summary>Read-only because the order is FINISHED, rather than because of what the user may do.</summary>
    private readonly bool _isFinalized;
    private bool _isRefunded;
    private readonly ObservableCollection<CustomMadeServiceRecord> _customMadeRecords = new();
    private readonly List<ClothingItemEditorRow> _clothingItemRows = new();
    private bool _suppressLanguageRefresh;
    private bool _syncingPayment;
    private bool _syncingStatus;
    // Guards EnforceDepositCeiling: its modal warning pumps messages and its correction raises
    // TextChanged again, so without this the dialog can stack up.
    private bool _enforcingDepositCeiling;
    private bool _alterationAutoCompleted;
    private bool _customMadeAutoCompleted;
    private bool _clothingAutoCompleted;
    private decimal _totalAmount;
    private decimal _alterationSubtotal;
    private decimal _alterationSumTotal;
    private decimal _clothingSubtotal;
    private decimal _clothingSumTotal;
    private decimal _customMadeSubtotal;
    private decimal _customMadeSumTotal;

    // Latest per-section money split (deposit vs final balance, each taxed by its own
    // method). Recomputed by the Refresh*Totals passes and reused by the summary.
    private SectionPayment _alterationMoney;
    private SectionPayment _clothingMoney;
    private SectionPayment _customMadeMoney;

    // Whether this order's amounts are quoted tax-inclusive. An EXISTING order keeps the mode it was
    // frozen with, so a historical receipt reprints unchanged; a NEW order takes the active shop's
    // current jurisdiction. Threaded into every live money split so the editor's totals match what
    // will be saved.
    private bool PricesIncludeTax
        => _existing?.PricesIncludeTax ?? TaxJurisdictions.PricesIncludeTax(ShopContext.Instance.Current);

    /// <summary>
    /// The rate an inclusive order's embedded tax is backed out at, from the shop's JURISDICTION.
    /// </summary>
    /// <remarks>
    /// Not from <see cref="PaymentTaxRules.Active"/>, which is where the exclusive branch gets its
    /// rates. A value-added tax is a property of the sale: it cannot differ between the deposit and
    /// the final balance, or between a cash and a card settlement of the same price. Reading it from
    /// the per-method matrix made it do both — and Shop Settings hides that matrix for an inclusive
    /// location, so the rate in force was one the shop could not even see.
    /// </remarks>
    private static decimal IncludedTaxRatePercent
        => TaxJurisdictions.IncludedTaxRatePercent(ShopContext.Instance.Current);

    // Groups the payment controls of a single service section so section-processing
    // methods take one logical parameter instead of a long positional list.
    private PaymentSectionControls _alterationControls = null!;
    private PaymentSectionControls _customMadeControls = null!;
    private PaymentSectionControls _clothingControls = null!;

    private sealed class PaymentSectionControls
    {
        public required RadioButton DownNone { get; init; }
        public required RadioButton DownEtransfer { get; init; }
        public required RadioButton DownDebit { get; init; }
        public required RadioButton DownCredit { get; init; }
        public required RadioButton DownCash { get; init; }
        public required TextBox DownpaymentBox { get; init; }
        public required CheckBox DownCompletedCheck { get; init; }
        public required RadioButton FinalEtransfer { get; init; }
        public required RadioButton FinalDebit { get; init; }
        public required RadioButton FinalCredit { get; init; }
        public required RadioButton FinalCash { get; init; }
        public required CheckBox BalanceClearedCheck { get; init; }
        public required UIElement PricingPanel { get; init; }
        public required UIElement FinalBlock { get; init; }
        // Deposit-stage breakdown panel (tax on deposit + post-tax total; hidden once deposit received).
        public required StackPanel DepositBreakdownPanel { get; init; }
        // Final-stage complete breakdown (shown inside FinalBlock once deposit is received).
        public required StackPanel FinalBreakdownPanel { get; init; }
        // Read-only display of the rate charged at the current stage. The rate itself is a
        // store-wide rule (PaymentTaxRules.Active), not something an order can override, so this
        // is a bold value rather than an input.
        public required TextBlock TaxValueText { get; init; }
        // Label beside TaxValueText; its text names the stage the rate applies to.
        public required TextBlock TaxLabel { get; init; }
        // Small print under the final-stage total tax: one line per payment portion.
        public required TextBlock DepositTaxLine { get; init; }
        public required TextBlock FinalTaxLine { get; init; }
        // What each portion COSTS, against what has actually been taken. The due figure shows from
        // the start; its received partner appears only once that portion is confirmed, so the pair
        // reads as a charge and then as a receipt rather than as one number that quietly changes
        // meaning. The deposit-stage panel carries the deposit's due line alone — by definition
        // nothing has been received while it is on screen.
        public required TextBlock DueDownpaymentText { get; init; }
        public required TextBlock FinalDueDownpaymentText { get; init; }
        public required TextBlock FinalReceivedDownpaymentText { get; init; }
        public required TextBlock FinalDueBalanceText { get; init; }
        public required TextBlock FinalReceivedBalanceText { get; init; }
        // Hidden alongside their values: a label with no figure beside it reads as a value that
        // failed to load rather than as a payment that has not happened yet.
        public required TextBlock FinalReceivedDownpaymentLabel { get; init; }
        public required TextBlock FinalReceivedBalanceLabel { get; init; }

        // The section's price and its deposit are quoted PRE-TAX where tax is added at settlement and
        // TAX-INCLUSIVE where it is not, so both labels are chosen per order rather than bound in
        // markup — mislabelling these is a wrong price, not a wrong word.
        public required TextBlock PriceLabel { get; init; }
        public required TextBlock DepositLabel { get; init; }

        // Tax-INCLUSIVE counterpart of FinalBreakdownPanel: the price, what has been taken, what is
        // owed, and one line naming the tax already inside the price. Exactly one of the two panels is
        // ever visible. See the markup for why they are separate rather than one panel with rows
        // collapsed.
        public required StackPanel FinalInclusivePanel { get; init; }
        public required TextBlock IncTotalText { get; init; }
        public required TextBlock IncReceivedDepositLabel { get; init; }
        public required TextBlock IncReceivedDepositText { get; init; }
        public required TextBlock IncDueBalanceText { get; init; }
        public required TextBlock IncResidualText { get; init; }
        public required TextBlock IncReceivedBalanceLabel { get; init; }
        public required TextBlock IncReceivedBalanceText { get; init; }
        // "Includes VAT (6%)" — the tax's own name for this location, and the rate it was carved out at.
        public required TextBlock IncTaxLabel { get; init; }
        public required TextBlock IncTaxText { get; init; }

        // ── Splitting one stage across payment types (v4.0) ──────────────────────────────────────
        //
        // The toggle is per SECTION and covers both of its stages, which is where every other payment
        // setting on this form already lives. Where the price contains the tax the whole thing is
        // hidden: a split cannot move a tax that is already inside the price.
        public required StackPanel SplitToggle { get; init; }
        public required RadioButton NoSplitRadio { get; init; }
        public required RadioButton SplitRadio { get; init; }
        // The single-method rows, hidden while split: choosing one method and several at once is a
        // contradiction, not a choice.
        public required StackPanel DownMethodRow { get; init; }
        public required StackPanel FinalMethodRow { get; init; }
        public required StackPanel DepositSplitPanel { get; init; }
        public required StackPanel DepositSplitRows { get; init; }
        public required TextBlock DepositSplitSummary { get; init; }
        public required StackPanel FinalSplitPanel { get; init; }
        public required StackPanel FinalSplitRows { get; init; }
        public required TextBlock FinalSplitSummary { get; init; }
        // The same choice offered again at the balance stage. Mirrored from the pair above rather than
        // holding a second flag: one section, one answer, shown in two places.
        public required StackPanel FinalSplitToggle { get; init; }
        public required RadioButton FinalNoSplitRadio { get; init; }
        public required RadioButton FinalSplitRadio { get; init; }

        /// <summary>The amount boxes, built in code so a new payment method needs no markup.</summary>
        public List<SplitRow> DepositRows { get; } = new();
        public List<SplitRow> FinalRows { get; } = new();

        /// <summary>
        /// Whether the DEPOSIT stage is split, and whether the FINAL one is — two independent answers.
        /// </summary>
        /// <remarks>
        /// They were one flag, with the balance stage's toggle mirrored onto the deposit's. That made
        /// the two radios move together, so deciding to split a balance rewrote how the deposit was
        /// recorded — a stage that had already been taken and confirmed.
        /// </remarks>
        public bool IsDepositSplit => SplitRadio.IsChecked is true;

        public bool IsFinalSplit => FinalSplitRadio.IsChecked is true;

        /// <summary>Whether the named stage is split.</summary>
        public bool IsSplitAt(bool finalStage) => finalStage ? IsFinalSplit : IsDepositSplit;

        // True when the section carries order items, which is what makes the service part of
        // this order at all. A section without items sits out the payment flow entirely; one
        // WITH items takes part even when it is priced at zero.
        public required Func<bool> HasItems { get; init; }
        // The section's current post-tax total (read live — the Refresh*Totals passes own it).
        public required Func<decimal> SectionTotal { get; init; }
        // The section's PRE-TAX subtotal, which is the ceiling for its deposit.
        public required Func<decimal> SectionSubtotal { get; init; }
        // Localization key naming this service.
        public required string ServiceNameKey { get; init; }

        // Priced at nothing despite carrying items: allowed, but worth flagging.
        public bool HasMissingPrice => HasItems() && SectionTotal() <= 0m;

        // True once the user has picked a final-balance method by hand. Until then the final
        // method just follows the deposit's, so changing the deposit method must re-mirror it.
        public bool FinalMethodUserChosen { get; set; }

        // Optional: the service has been switched off for this order (Alterations "None"), so
        // its inputs lock and it contributes nothing. Sections without the concept omit it.
        public Func<bool>? ServiceSwitchedOff { get; init; }

        public bool IsServiceSwitchedOff => ServiceSwitchedOff?.Invoke() ?? false;

        // Both stage rates are held here and only one is on screen at a time. ShowingFinalRate
        // records which, so the display can follow the section from the deposit stage to the
        // final one without either rate being lost.
        public decimal DepositTaxRate { get; set; }
        public decimal FinalTaxRate { get; set; }
        public bool ShowingFinalRate { get; set; }

        // The deposit is settled and the outstanding balance is what the shop is now
        // charging: either the deposit was marked received, or "None" means no deposit was
        // taken at all. This is the stage whose rate is displayed.
        public bool IsFinalStage => DownNone.IsChecked is true || DownCompletedCheck.IsChecked is true;
    }

    /// <summary>
    /// One payment type's line inside a split: the amount taken by it, and the tax that amount carries.
    /// </summary>
    /// <remarks>
    /// Built in code from <c>PaymentTaxRules.ConfigurableMethods</c> rather than written out in markup,
    /// the same way the shop's tax matrix is: three sections times two stages times four methods is
    /// twenty-four rows of XAML that would all have to be found and edited to add a fifth method.
    /// </remarks>
    private sealed record SplitRow(PaymentMethod Method, TextBox Amount, TextBlock Detail, TextBlock Placeholder)
    {
        public decimal Value => ParseDecimalOrZero(Amount.Text);

        /// <summary>
        /// True when nothing has been typed here. Distinct from an amount of zero: a blank row is one
        /// the shop has not answered yet and can still be offered the remainder, while a typed 0 is an
        /// answer — "nothing was taken this way" — and must not be overwritten.
        /// </summary>
        public bool IsBlank => Amount.Text.Trim().Length == 0;
    }

    public OrderEditWindow(IServiceScopeFactory scopeFactory, LocalizationService localization)
    {
        InitializeComponent();
        _scopeFactory = scopeFactory;
        _localization = localization;

        InitializeCommonControls();
        _existing = null;
        _isReadOnly = false;

        // Built from the shop's configured receipt format (Local Configuration → Shop Settings). Only a preview:
        // the running number is not reserved until the order is actually saved, so closing this
        // window without saving cannot leave a gap in the shop's receipt run.
        OrderNumberBox.Text = OrderNumberFormatter.Preview(ShopContext.Instance.RequireCurrent(), DateTime.Now);
        CustomerNameBox.Text = string.Empty;
        // Opens on the country this shop's customers usually call from — its LOCATION, not its
        // currency, which is only the fallback for a shop that never said where it is.
        PhoneField.ResetTo(ShopContext.Instance.Current);
        EmailBox.Text = string.Empty;
        AddressBox.Text = string.Empty;
        StatusReasonBox.Text = string.Empty;
        StatusReasonCategoryBox.SelectedIndex = 0;
        TotalAmountText.Text = FormatCurrency(0m);
        // Tax rates are not seeded here any more: RefreshComputedTotals resolves each section's
        // rate from the shop's rules and writes it into the read-only value blocks.
        StatusBox.SelectedIndex = 0;
        UpdateStatusReasonVisibility();
        AlterationCategoryBox.SelectedIndex = 0;
        AlterationsRadio.IsChecked = true;
        _syncingPayment = true;
        AlterationDownCash.IsChecked = true;
        CustomMadeDownCash.IsChecked = true;
        ClothingDownCash.IsChecked = true;
        RefreshServicePanels();
        UpdatePaymentVisibility();
        _syncingPayment = false;
        RefreshComputedTotals();
        _localization.LanguageChanged += OnLanguageChangedGlobally;
    }

    public OrderEditWindow(IServiceScopeFactory scopeFactory, LocalizationService localization, Order existing)
    {
        InitializeComponent();
        _scopeFactory = scopeFactory;
        _localization = localization;
        _existing = existing;

        // Two different reasons to refuse an edit, and the notice has to say WHICH: the order is
        // finished, or this user may read orders but not change them. Both land in the same
        // read-only mode, because "what the window does" and "why" are separate questions.
        _isFinalized = IsReadOnlyStatus(existing.Status);
        _isReadOnly = _isFinalized || !AuthenticationService.Instance.CanEditOrders;
        _isRefunded = existing.IsRefunded;

        InitializeCommonControls();

        Title = _localization[_isReadOnly ? ViewTitleKey : EditTitleKey];
        TitleText.Text = _localization[_isReadOnly ? ViewTitleKey : EditTitleKey];
        OrderNumberBox.Text = existing.OrderNumber;
        OrderNumberBox.IsEnabled = false;
        CustomerNameBox.Text = existing.CustomerName;
        // A stored number that names no country — everything saved before this control existed — comes
        // back whole, under the shop's country, and is not rewritten.
        PhoneField.Load(existing.PhoneNumber, ShopContext.Instance.Current);
        EmailBox.Text = existing.Email;
        AddressBox.Text = existing.Address;
        StatusReasonBox.Text = existing.StatusReason;
        LoadStatusReasonCategory(existing.StatusReasonCategory);
        TotalAmountText.Text = FormatCurrency(existing.TotalAmount);
        NotesBox.Text = existing.Notes;
        var matchedCategory = false;
        foreach (var categoryItem in AlterationCategoryBox.Items.OfType<ComboBoxItem>())
        {
            var isMatch = string.Equals(categoryItem.Tag as string, existing.ServiceDetails, StringComparison.Ordinal);
            categoryItem.IsSelected = isMatch;
            matchedCategory |= isMatch;
        }
        // Deliberately NOT the first item here: the first item is "None", which switches the
        // alteration service off. A saved order that stored free text (from before this
        // dropdown existed) or no category at all must not silently lose its alteration
        // charge, so an unmatched value falls back to the plain garment-adjustments category.
        if (!matchedCategory)
            SelectAlterationCategory(DefaultSavedAlterationCategoryTag);
        AlterationAdditionalNotesBox.Text = existing.AdditionalNotes;
        AlterationPriceBox.Text = (existing.AlterationSubtotal ?? existing.Subtotal)?.ToString("0.##") ?? string.Empty;

        if (existing.ServiceType == OrderServiceType.Alterations && string.IsNullOrWhiteSpace(AlterationPriceBox.Text))
        {
            // Read the stored rate directly: the tax boxes are only populated further down
            // (after LoadPaymentFields), so they are still empty at this point.
            var effectiveTaxRate = existing.AlterationTaxRate ?? existing.TaxRate ?? DefaultTaxRate;
            var subtotalFromTotal = effectiveTaxRate > 0m
                ? existing.TotalAmount / (1m + (effectiveTaxRate / 100m))
                : existing.TotalAmount;

            AlterationPriceBox.Text = subtotalFromTotal.ToString("0.00");
        }

        LoadCustomMadeRecords(existing.CustomMadeRecordsJson);

        foreach (var item in existing.Items)
            AddClothingItemRow(item);

        SelectServiceType(existing.ServiceType);

        LoadPaymentFields(existing);
        LoadPaymentSplits(existing);
        LoadStageTaxRates(existing);

        foreach (ComboBoxItem item in StatusBox.Items)
        {
            if (item.Tag?.ToString() == existing.Status.ToString())
            {
                StatusBox.SelectedItem = item;
                break;
            }
        }
        UpdateStatusReasonVisibility();

        RefreshComputedTotals();

        RefreshCustomMadeEmptyState();
        _localization.LanguageChanged += OnLanguageChangedGlobally;

        if (_isReadOnly)
            ApplyReadOnlyMode();

        // Mark the "not applicable" checkboxes for an already cancelled/returned order.
        if (_isRefunded)
            ApplyNotApplicableCheckboxStyle(true);
    }

    // Shipped is treated as a finalized/completed state (the order has already been
    // delivered to the customer), so it is read-only just like Completed/Cancelled/Returned.
    private static bool IsReadOnlyStatus(OrderStatus status)
        => status is OrderStatus.Shipped or OrderStatus.Completed or OrderStatus.Cancelled or OrderStatus.Returned;

    /// <summary>Which of the two read-only reasons the notice should state.</summary>
    private string ReadOnlyNoticeKey
        => _isFinalized ? "OrderEdit.ReadOnlyNotice" : "OrderEdit.ReadOnlyNoRight";

    private void ApplyReadOnlyMode()
    {
        SaveButton.Visibility = Visibility.Collapsed;
        ReadOnlyNotice.Visibility = Visibility.Visible;

        // Assigned rather than left to the XAML binding, which can only name one of the two reasons.
        // RefreshLocalizedLabels re-applies it, since a code-set value replaces the binding and
        // would otherwise stop following a language switch.
        ReadOnlyNotice.Text = _localization[ReadOnlyNoticeKey];

        StatusBox.IsEnabled = false;
        PickedUpCheck.IsEnabled = false;
        ClearAllBalancesCheck.IsEnabled = false;

        OrderDatePicker.IsEnabled = false;
        PickupDatePicker.IsEnabled = false;
        CustomerNameBox.IsReadOnly = true;
        PhoneField.IsReadOnlyField = true;
        EmailBox.IsReadOnly = true;
        AddressBox.IsReadOnly = true;
        StatusReasonBox.IsReadOnly = true;
        StatusReasonCategoryBox.IsEnabled = false;
        NotesBox.IsReadOnly = true;

        AlterationCategoryBox.IsEnabled = false;
        AlterationAdditionalNotesBox.IsReadOnly = true;
        AlterationPriceBox.IsReadOnly = true;
        AlterationDownpaymentBox.IsReadOnly = true;
        CustomMadeDownpaymentBox.IsReadOnly = true;
        ClothingDownpaymentBox.IsReadOnly = true;

        AlterationDownCompletedCheck.IsEnabled = false;
        AlterationBalanceClearedCheck.IsEnabled = false;
        CustomMadeDownCompletedCheck.IsEnabled = false;
        CustomMadeBalanceClearedCheck.IsEnabled = false;
        ClothingDownCompletedCheck.IsEnabled = false;
        ClothingBalanceClearedCheck.IsEnabled = false;

        AddItemButton.IsEnabled = false;
        AddCustomMadeButton.IsEnabled = false;
        RemoveCustomMadeButton.IsEnabled = false;

        SetReadOnlyPaymentSection(_alterationControls);
        SetReadOnlyPaymentSection(_customMadeControls);
        SetReadOnlyPaymentSection(_clothingControls);
        ApplyReadOnlyModeToClothingRows();
    }

    private static void SetReadOnlyPaymentSection(PaymentSectionControls section)
        => SetPaymentSectionEnabled(section, false);

    private static void SetPaymentSectionEnabled(PaymentSectionControls section, bool enabled)
    {
        section.DownNone.IsEnabled = enabled;
        section.DownEtransfer.IsEnabled = enabled;
        section.DownDebit.IsEnabled = enabled;
        section.DownCredit.IsEnabled = enabled;
        section.DownCash.IsEnabled = enabled;
        section.DownCompletedCheck.IsEnabled = enabled;
        section.FinalEtransfer.IsEnabled = enabled;
        section.FinalDebit.IsEnabled = enabled;
        section.FinalCredit.IsEnabled = enabled;
        section.FinalCash.IsEnabled = enabled;
        section.BalanceClearedCheck.IsEnabled = enabled;
        section.DownpaymentBox.IsReadOnly = !enabled;
    }

    // Locks (or restores) every service / payment editing control when an order is
    // toggled to / from a refunded (cancelled/returned) status while still editable.
    // Customer fields stay editable; only the services are locked, and the custom-made
    // records list stays viewable so measurements can still be inspected.
    private void SetServiceControlsEnabled(bool enabled)
    {
        SetPaymentSectionEnabled(_alterationControls, enabled);
        SetPaymentSectionEnabled(_customMadeControls, enabled);
        SetPaymentSectionEnabled(_clothingControls, enabled);

        AlterationCategoryBox.IsEnabled = enabled;
        AlterationAdditionalNotesBox.IsReadOnly = !enabled;
        AlterationPriceBox.IsReadOnly = !enabled;

        AddItemButton.IsEnabled = enabled;
        AddCustomMadeButton.IsEnabled = enabled;
        RemoveCustomMadeButton.IsEnabled = enabled;
        ClearAllBalancesCheck.IsEnabled = enabled;
        SetClothingRowsLocked(!enabled);
    }

    // Applies / removes the red strikethrough "not applicable" styling on every service
    // and quick-operation checkbox (including OrderEdit.PickedUp and OrderEdit.BalanceCleared). Each checkbox's
    // strike line is a sibling Border (bound to that checkbox's own ActualWidth — see
    // NotApplicableCheckBoxStrike in XAML) toggled alongside the Style swap, so the line
    // always matches the checkbox + label width instead of the whole row.
    private void ApplyNotApplicableCheckboxStyle(bool notApplicable)
    {
        var style = notApplicable ? (Style)FindResource("NotApplicableCheckBox") : null;
        var strikeVisibility = notApplicable ? Visibility.Visible : Visibility.Collapsed;

        SetNotApplicableCheckbox(ClearAllBalancesCheck, ClearAllBalancesStrike, style, strikeVisibility);
        SetNotApplicableCheckbox(PickedUpCheck, PickedUpStrike, style, strikeVisibility);
        SetNotApplicableCheckbox(AlterationDownCompletedCheck, AlterationDownCompletedStrike, style, strikeVisibility);
        SetNotApplicableCheckbox(AlterationBalanceClearedCheck, AlterationBalanceClearedStrike, style, strikeVisibility);
        SetNotApplicableCheckbox(CustomMadeDownCompletedCheck, CustomMadeDownCompletedStrike, style, strikeVisibility);
        SetNotApplicableCheckbox(CustomMadeBalanceClearedCheck, CustomMadeBalanceClearedStrike, style, strikeVisibility);
        SetNotApplicableCheckbox(ClothingDownCompletedCheck, ClothingDownCompletedStrike, style, strikeVisibility);
        SetNotApplicableCheckbox(ClothingBalanceClearedCheck, ClothingBalanceClearedStrike, style, strikeVisibility);
    }

    private static void SetNotApplicableCheckbox(CheckBox checkbox, Border strike, Style? style, Visibility strikeVisibility)
    {
        checkbox.Style = style;
        strike.Visibility = strikeVisibility;
    }

    // Applies or reverts the dynamic refund lock when the status dropdown is switched
    // to / from Cancelled / Returned on an order that is still editable.
    private void ApplyRefundLockState()
    {
        if (_isReadOnly)
            return;

        if (_isRefunded)
        {
            SetServiceControlsEnabled(false);
        }
        else
        {
            SetServiceControlsEnabled(true);
            RefreshComputedTotals(runAutoComplete: false);
        }

        ApplyNotApplicableCheckboxStyle(_isRefunded);
        RefreshPaymentSummary();
    }

    private void ApplyReadOnlyModeToClothingRows()
    {
        foreach (var row in _clothingItemRows)
        {
            row.CategoryBox.IsEnabled = false;
            row.UnitPriceBox.IsReadOnly = true;
            row.PromotionalPriceBox.IsReadOnly = true;
            row.RemoveButton.IsEnabled = false;
        }
    }

    private void InitializeCommonControls()
    {
        InitializePaymentSectionControls();
        BuildSplitRows();
        RegisterDecimalTextBoxes();
        RegisterValidationClearing();
        InitializeCustomMadeRecordsList();
        InitializeCurrencyChoices();
        InitializeOrderDatePicker();
        SelectServiceType(OrderServiceType.Alterations);
        RefreshLocalizedLabels();
    }

    /// <summary>
    /// Seeds the order-date picker and stops it offering a day that has not happened yet.
    /// </summary>
    /// <remarks>
    /// The picker opens on the date the order already carries — today for a new one — so a save that
    /// never touches it records exactly what it recorded before this field existed.
    ///
    /// The future is BLACKED OUT rather than cut off with <c>DisplayDateEnd</c>. Both refuse it, but
    /// DisplayDateEnd makes the days after it cease to exist, and rendering the drop-down showed what
    /// that looks like on the 1st of a month: a calendar headed "August 2026" containing a single
    /// day. A blacked-out day is still drawn, struck through, which reads as "not that one" instead
    /// of as a fault. It also refuses a TYPED date, which DisplayDateEnd was measured NOT to do.
    ///
    /// The boundary is the LATER of today and the stored date. Nothing in the app produces a future
    /// order date, but the GraphQL API accepts any <c>DateTime</c> a caller sends, and blacking out
    /// the date an order already carries throws rather than merely looking odd.
    /// </remarks>
    private void InitializeOrderDatePicker()
    {
        var recorded = RecordedOrderDate().ToLocalTime().Date;
        var lastAllowed = recorded > DateTime.Today ? recorded : DateTime.Today;

        OrderDatePicker.SelectedDate = recorded;
        OrderDatePicker.BlackoutDates.Add(new CalendarDateRange(lastAllowed.AddDays(1), DateTime.MaxValue));

        InitializePickupDatePicker();
    }

    /// <summary>
    /// Seeds the pickup picker and blacks out everything that is not in the future.
    /// </summary>
    /// <remarks>
    /// The mirror image of the order date, and empty where that one is seeded: a pickup date is
    /// something the shop AGREED with a customer, so there is no sensible default. It stays blank
    /// until somebody fills it in, and the save refuses to go without it.
    ///
    /// The boundary is the EARLIER of tomorrow and whatever the order already carries. Orders taken
    /// before this field existed have none, and one whose promised day has since passed must still
    /// open, and still save, without being forced to a new date the customer never agreed to.
    /// </remarks>
    private void InitializePickupDatePicker()
    {
        var recorded = _existing?.ExpectedPickupDateLocal?.Date;
        var firstAllowed = recorded is { } day && day < DateTime.Today.AddDays(1)
            ? day
            : DateTime.Today.AddDays(1);

        PickupDatePicker.SelectedDate = recorded;
        PickupDatePicker.BlackoutDates.Add(
            new CalendarDateRange(DateTime.MinValue, firstAllowed.AddDays(-1)));
    }

    /// <summary>The order date as it stands before this form is saved — <c>UtcNow</c> for a new one.</summary>
    private DateTime RecordedOrderDate() => _existing?.OrderDate ?? DateTime.UtcNow;

    /// <summary>
    /// Puts the calendar drop-down into the current UI language.
    /// </summary>
    /// <remarks>
    /// A <c>Calendar</c> renders its month and day names from <c>FrameworkElement.Language</c> rather
    /// than from the string table, so without this it stays in the OS language whatever the shop
    /// picked. Set on the window because Language is inherited, and re-set on every language change
    /// because this window stays open across one. Same line as <c>StoreMembersWindow</c>'s.
    /// </remarks>
    private void ApplyCalendarLanguage()
        => Language = XmlLanguage.GetLanguage(_localization.CurrentLanguageCode);

    /// <summary>
    /// Fills the currency picker from the currencies the open shop accepts, and hides it outright
    /// when that is only one.
    /// </summary>
    /// <remarks>
    /// An order being EDITED keeps its own currency in the list even if the shop has since stopped
    /// accepting it. Dropping it would leave the picker showing some other currency beside unchanged
    /// amounts, and saving would then silently re-denominate a finished order — turning ￥1,695 into
    /// $1,695 because a setting changed months later. What a shop takes today does not reach back
    /// and restate what it charged.
    /// </remarks>
    private void InitializeCurrencyChoices()
    {
        var shop = ShopContext.Instance.Current;
        var currencies = ShopCurrencies.Supported(shop).ToList();

        if (_existing is not null && !currencies.Contains(_existing.CurrencyType))
            currencies.Insert(0, _existing.CurrencyType);

        CurrencyBox.SelectedValuePath = nameof(ComboBoxItem.Tag);
        CurrencyBox.Items.Clear();
        foreach (var currency in currencies)
        {
            CurrencyBox.Items.Add(new ComboBoxItem
            {
                Content = ShopCurrencies.Name(currency, _localization),
                Tag = currency,
            });
        }

        CurrencyBox.SelectedValue = _existing?.CurrencyType ?? ShopCurrencies.Preferred(shop);
        if (CurrencyBox.SelectedIndex < 0)
            CurrencyBox.SelectedIndex = 0;

        // Hidden rather than disabled, per the convention every other gated control here follows.
        var visibility = currencies.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        CurrencyBox.Visibility = visibility;
        CurrencyLabelPanel.Visibility = visibility;
    }

    /// <summary>The currency the form is pricing in — what a save will stamp onto the order.</summary>
    private CurrencyType SelectedCurrency
        => CurrencyBox.SelectedValue as CurrencyType?
           ?? _existing?.CurrencyType
           ?? ShopCurrencies.Preferred(ShopContext.Instance.Current);

    private void OnCurrencyChanged(object sender, SelectionChangedEventArgs e)
    {
        // Every amount on the form is rendered through FormatCurrency, so the symbols only follow
        // the picker if the totals are rebuilt. Guarded because this fires while the window is still
        // being constructed, before the payment controls exist.
        if (_alterationControls is not null)
            RefreshComputedTotals(runAutoComplete: false);
    }

    private void InitializePaymentSectionControls()
    {
        _alterationControls = new PaymentSectionControls
        {
            DownNone = AlterationDownNone,
            DownEtransfer = AlterationDownEtransfer,
            DownDebit = AlterationDownDebit,
            DownCredit = AlterationDownCredit,
            DownCash = AlterationDownCash,
            DownpaymentBox = AlterationDownpaymentBox,
            DownCompletedCheck = AlterationDownCompletedCheck,
            FinalEtransfer = AlterationFinalEtransfer,
            FinalDebit = AlterationFinalDebit,
            FinalCredit = AlterationFinalCredit,
            FinalCash = AlterationFinalCash,
            BalanceClearedCheck = AlterationBalanceClearedCheck,
            PricingPanel = AlterationPricingPanel,
            FinalBlock = AlterationFinalBlock,
            DepositBreakdownPanel = AlterationDepositBreakdownPanel,
            FinalBreakdownPanel = AlterationFinalBreakdownPanel,
            TaxValueText = AlterationTaxValueText,
            TaxLabel = AlterationTaxLabel,
            DepositTaxLine = AlterationDepositTaxLineText,
            FinalTaxLine = AlterationFinalTaxLineText,
            DueDownpaymentText = AlterationDueDownpaymentText,
            FinalDueDownpaymentText = AlterationFinalDueDownpaymentText,
            FinalReceivedDownpaymentText = AlterationFinalReceivedDownpaymentText,
            FinalDueBalanceText = AlterationFinalDueBalanceText,
            FinalReceivedBalanceText = AlterationFinalReceivedBalanceText,
            FinalReceivedDownpaymentLabel = AlterationFinalReceivedDownpaymentLabel,
            FinalReceivedBalanceLabel = AlterationFinalReceivedBalanceLabel,
            PriceLabel = AlterationPriceLabel,
            DepositLabel = AlterationDepositLabel,
            FinalInclusivePanel = AlterationFinalInclusivePanel,
            IncTotalText = AlterationIncTotalText,
            IncReceivedDepositLabel = AlterationIncReceivedDepositLabel,
            IncReceivedDepositText = AlterationIncReceivedDepositText,
            IncDueBalanceText = AlterationIncDueBalanceText,
            IncResidualText = AlterationIncResidualText,
            IncReceivedBalanceLabel = AlterationIncReceivedBalanceLabel,
            IncReceivedBalanceText = AlterationIncReceivedBalanceText,
            IncTaxLabel = AlterationIncTaxLabel,
            IncTaxText = AlterationIncTaxText,
            SplitToggle = AlterationSplitToggle,
            NoSplitRadio = AlterationNoSplitRadio,
            SplitRadio = AlterationSplitRadio,
            DownMethodRow = AlterationDownMethodRow,
            FinalMethodRow = AlterationFinalMethodRow,
            DepositSplitPanel = AlterationDepositSplitPanel,
            DepositSplitRows = AlterationDepositSplitRows,
            DepositSplitSummary = AlterationDepositSplitSummary,
            FinalSplitPanel = AlterationFinalSplitPanel,
            FinalSplitRows = AlterationFinalSplitRows,
            FinalSplitSummary = AlterationFinalSplitSummary,
            FinalSplitToggle = AlterationFinalSplitToggle,
            FinalNoSplitRadio = AlterationFinalNoSplitRadio,
            FinalSplitRadio = AlterationFinalSplitRadio,
            // Alterations has no item list of its own, so a typed price — even "0" — is what
            // marks the service as present on this order. Choosing the "None" category switches
            // the service off outright, so it stops counting whatever the price box holds.
            HasItems = () => !AlterationServiceSwitchedOff && !string.IsNullOrWhiteSpace(AlterationPriceBox.Text),
            SectionTotal = () => _alterationSumTotal,
            SectionSubtotal = () => _alterationSubtotal,
            ServiceNameKey = "ServiceType.Alterations",
            ServiceSwitchedOff = () => AlterationServiceSwitchedOff
        };
        _customMadeControls = new PaymentSectionControls
        {
            DownNone = CustomMadeDownNone,
            DownEtransfer = CustomMadeDownEtransfer,
            DownDebit = CustomMadeDownDebit,
            DownCredit = CustomMadeDownCredit,
            DownCash = CustomMadeDownCash,
            DownpaymentBox = CustomMadeDownpaymentBox,
            DownCompletedCheck = CustomMadeDownCompletedCheck,
            FinalEtransfer = CustomMadeFinalEtransfer,
            FinalDebit = CustomMadeFinalDebit,
            FinalCredit = CustomMadeFinalCredit,
            FinalCash = CustomMadeFinalCash,
            BalanceClearedCheck = CustomMadeBalanceClearedCheck,
            PricingPanel = CustomMadePricingPanel,
            FinalBlock = CustomMadeFinalBlock,
            DepositBreakdownPanel = CustomMadeDepositBreakdownPanel,
            FinalBreakdownPanel = CustomMadeFinalBreakdownPanel,
            TaxValueText = CustomMadeTaxValueText,
            TaxLabel = CustomMadeTaxLabel,
            DepositTaxLine = CustomMadeDepositTaxLineText,
            FinalTaxLine = CustomMadeFinalTaxLineText,
            DueDownpaymentText = CustomMadeDueDownpaymentText,
            FinalDueDownpaymentText = CustomMadeFinalDueDownpaymentText,
            FinalReceivedDownpaymentText = CustomMadeFinalReceivedDownpaymentText,
            FinalDueBalanceText = CustomMadeFinalDueBalanceText,
            FinalReceivedBalanceText = CustomMadeFinalReceivedBalanceText,
            FinalReceivedDownpaymentLabel = CustomMadeFinalReceivedDownpaymentLabel,
            FinalReceivedBalanceLabel = CustomMadeFinalReceivedBalanceLabel,
            PriceLabel = CustomMadePriceLabel,
            DepositLabel = CustomMadeDepositLabel,
            FinalInclusivePanel = CustomMadeFinalInclusivePanel,
            IncTotalText = CustomMadeIncTotalText,
            IncReceivedDepositLabel = CustomMadeIncReceivedDepositLabel,
            IncReceivedDepositText = CustomMadeIncReceivedDepositText,
            IncDueBalanceText = CustomMadeIncDueBalanceText,
            IncResidualText = CustomMadeIncResidualText,
            IncReceivedBalanceLabel = CustomMadeIncReceivedBalanceLabel,
            IncReceivedBalanceText = CustomMadeIncReceivedBalanceText,
            IncTaxLabel = CustomMadeIncTaxLabel,
            IncTaxText = CustomMadeIncTaxText,
            SplitToggle = CustomMadeSplitToggle,
            NoSplitRadio = CustomMadeNoSplitRadio,
            SplitRadio = CustomMadeSplitRadio,
            DownMethodRow = CustomMadeDownMethodRow,
            FinalMethodRow = CustomMadeFinalMethodRow,
            DepositSplitPanel = CustomMadeDepositSplitPanel,
            DepositSplitRows = CustomMadeDepositSplitRows,
            DepositSplitSummary = CustomMadeDepositSplitSummary,
            FinalSplitPanel = CustomMadeFinalSplitPanel,
            FinalSplitRows = CustomMadeFinalSplitRows,
            FinalSplitSummary = CustomMadeFinalSplitSummary,
            FinalSplitToggle = CustomMadeFinalSplitToggle,
            FinalNoSplitRadio = CustomMadeFinalNoSplitRadio,
            FinalSplitRadio = CustomMadeFinalSplitRadio,
            HasItems = () => _customMadeRecords.Count > 0,
            SectionTotal = () => _customMadeSumTotal,
            SectionSubtotal = () => _customMadeSubtotal,
            ServiceNameKey = "ServiceType.CustomMade"
        };
        _clothingControls = new PaymentSectionControls
        {
            DownNone = ClothingDownNone,
            DownEtransfer = ClothingDownEtransfer,
            DownDebit = ClothingDownDebit,
            DownCredit = ClothingDownCredit,
            DownCash = ClothingDownCash,
            DownpaymentBox = ClothingDownpaymentBox,
            DownCompletedCheck = ClothingDownCompletedCheck,
            FinalEtransfer = ClothingFinalEtransfer,
            FinalDebit = ClothingFinalDebit,
            FinalCredit = ClothingFinalCredit,
            FinalCash = ClothingFinalCash,
            BalanceClearedCheck = ClothingBalanceClearedCheck,
            PricingPanel = ClothingPricingPanel,
            FinalBlock = ClothingFinalBlock,
            DepositBreakdownPanel = ClothingDepositBreakdownPanel,
            FinalBreakdownPanel = ClothingFinalBreakdownPanel,
            TaxValueText = ClothingTaxValueText,
            TaxLabel = ClothingTaxLabel,
            DepositTaxLine = ClothingDepositTaxLineText,
            FinalTaxLine = ClothingFinalTaxLineText,
            DueDownpaymentText = ClothingDueDownpaymentText,
            FinalDueDownpaymentText = ClothingFinalDueDownpaymentText,
            FinalReceivedDownpaymentText = ClothingFinalReceivedDownpaymentText,
            FinalDueBalanceText = ClothingFinalDueBalanceText,
            FinalReceivedBalanceText = ClothingFinalReceivedBalanceText,
            FinalReceivedDownpaymentLabel = ClothingFinalReceivedDownpaymentLabel,
            FinalReceivedBalanceLabel = ClothingFinalReceivedBalanceLabel,
            PriceLabel = ClothingPriceLabel,
            DepositLabel = ClothingDepositLabel,
            FinalInclusivePanel = ClothingFinalInclusivePanel,
            IncTotalText = ClothingIncTotalText,
            IncReceivedDepositLabel = ClothingIncReceivedDepositLabel,
            IncReceivedDepositText = ClothingIncReceivedDepositText,
            IncDueBalanceText = ClothingIncDueBalanceText,
            IncResidualText = ClothingIncResidualText,
            IncReceivedBalanceLabel = ClothingIncReceivedBalanceLabel,
            IncReceivedBalanceText = ClothingIncReceivedBalanceText,
            IncTaxLabel = ClothingIncTaxLabel,
            IncTaxText = ClothingIncTaxText,
            SplitToggle = ClothingSplitToggle,
            NoSplitRadio = ClothingNoSplitRadio,
            SplitRadio = ClothingSplitRadio,
            DownMethodRow = ClothingDownMethodRow,
            FinalMethodRow = ClothingFinalMethodRow,
            DepositSplitPanel = ClothingDepositSplitPanel,
            DepositSplitRows = ClothingDepositSplitRows,
            DepositSplitSummary = ClothingDepositSplitSummary,
            FinalSplitPanel = ClothingFinalSplitPanel,
            FinalSplitRows = ClothingFinalSplitRows,
            FinalSplitSummary = ClothingFinalSplitSummary,
            FinalSplitToggle = ClothingFinalSplitToggle,
            FinalNoSplitRadio = ClothingFinalNoSplitRadio,
            FinalSplitRadio = ClothingFinalSplitRadio,
            HasItems = () => _clothingItemRows.Count > 0,
            SectionTotal = () => _clothingSumTotal,
            SectionSubtotal = () => _clothingSubtotal,
            ServiceNameKey = "ServiceType.ReadyMade"
        };
    }

    private void OnLanguageChangedGlobally(object? sender, EventArgs e)
    {
        if (_suppressLanguageRefresh)
            return;

        _suppressLanguageRefresh = true;
        try
        {
            RefreshLocalizedLabels();
        }
        finally
        {
            _suppressLanguageRefresh = false;
        }
    }

    private void RefreshLocalizedLabels()
    {
        if (_existing is null)
        {
            Title = _localization["OrderEdit.NewTitle"];
            TitleText.Text = _localization["OrderEdit.NewTitle"];
        }
        else
        {
            var titleKey = _isReadOnly ? ViewTitleKey : EditTitleKey;
            Title = _localization[titleKey];
            TitleText.Text = _localization[titleKey];
        }

        if (_isReadOnly)
            ReadOnlyNotice.Text = _localization[ReadOnlyNoticeKey];

        RefreshCustomMadeButtonLabel();
        ApplyCalendarLanguage();

        RefreshServicePanels();
        RefreshCustomMadeEmptyState();
        RefreshPaymentLabels();
        UpdateStatusReasonVisibility();
        RefreshComputedTotals();
    }

    private void RefreshPaymentLabels()
    {
        AlterationPaymentTitle.Text = _localization.Format("OrderEdit.PaymentTitle", _localization["ServiceType.Alterations"]);
        CustomMadePaymentTitle.Text = _localization.Format("OrderEdit.PaymentTitle", _localization["ServiceType.CustomMade"]);
        ClothingPaymentTitle.Text = _localization.Format("OrderEdit.PaymentTitle", _localization["ServiceType.ReadyMade"]);

        var downLabel = _localization[DownpaymentMethodKey];
        var finalLabel = _localization[FinalBalanceMethodKey];

        AlterationDownMethodLabel.Text = downLabel;
        AlterationFinalMethodLabel.Text = finalLabel;
        CustomMadeDownMethodLabel.Text = downLabel;
        CustomMadeFinalMethodLabel.Text = finalLabel;
        ClothingDownMethodLabel.Text = downLabel;
        ClothingFinalMethodLabel.Text = finalLabel;

        // What the price and deposit boxes actually hold depends on the order's pricing mode, so they
        // are named here rather than bound in markup. Set from THIS order's frozen mode, not the
        // shop's current one: a receipt and the screen behind it must agree about a saved order even
        // after the shop moves.
        var priceLabel = _localization[PricesIncludeTax
            ? "Order.Fields.InclusiveServiceTotal"
            : "Order.Fields.PreTaxServiceTotal"];
        var depositLabel = _localization[PricesIncludeTax
            ? "Order.Fields.InclusiveDownpayment"
            : "Order.Fields.PreTaxDownpayment"];

        foreach (var section in AllPaymentSections)
        {
            section.PriceLabel.Text = priceLabel;
            section.DepositLabel.Text = depositLabel;
        }
    }

    /// <summary>The three service sections, for the settings that apply to all of them alike.</summary>
    private PaymentSectionControls[] AllPaymentSections
        => new[] { _alterationControls, _customMadeControls, _clothingControls };

    /// <summary>The lines a stage is currently carrying, as the calculation wants them.</summary>
    /// <remarks>
    /// Only rows with money in them. A method the shop has since made tax free still appears with its
    /// rate here; <c>Order.PortionTax</c> is what decides it charges nothing, so the editor and a
    /// saved order agree about which rule zeroed it.
    /// </remarks>
    private static IReadOnlyList<PaymentSplitLine> ReadSplitLines(PaymentSectionControls c, bool finalStage)
    {
        var rows = finalStage ? c.FinalRows : c.DepositRows;
        var rules = PaymentTaxRules.Active;

        return rows
            .Where(row => row.Value > 0m)
            .Select(row => new PaymentSplitLine
            {
                Method = row.Method,
                Amount = row.Value,
                RatePercent = rules.RateFor(row.Method),
            })
            .ToList();
    }

    /// <summary>False until the section controls are built, so parse-time events cannot reach them.</summary>
    private bool _sectionsReady;

    /// <summary>Where one split amount box sits: which section, which stage, and among which rows.</summary>
    private readonly record struct SplitSlot(
        PaymentSectionControls Section, bool FinalStage, List<SplitRow> Rows, SplitRow Row);

    /// <summary>
    /// Finds the row a focused amount box belongs to, across every section and both stages.
    /// </summary>
    /// <remarks>
    /// Separated from the handler because SEARCHING and ACTING were interleaved there: two nested loops
    /// wrapped around the fill, with the guards inside them, which carried the method past the
    /// cognitive-complexity limit. Neither half is complicated on its own.
    /// </remarks>
    private SplitSlot? FindSplitSlot(TextBox box)
    {
        foreach (var section in AllPaymentSections)
        {
            foreach (var finalStage in new[] { false, true })
            {
                var rows = finalStage ? section.FinalRows : section.DepositRows;
                var row = rows.Find(candidate => ReferenceEquals(candidate.Amount, box));

                if (row is not null)
                    return new SplitSlot(section, finalStage, rows, row);
            }
        }

        return null;
    }

    // Green once a stage's allocation balances, amber while it does not. Through the file's OWN brush
    // helper rather than a second one taking hex: two helpers doing one job is how they drift.
    private static readonly Brush BalancedSplitBrush = CreateFrozenBrush(0x04, 0x78, 0x57);
    private static readonly Brush UnbalancedSplitBrush = CreateFrozenBrush(0xB4, 0x53, 0x09);

    // The label colour on a split row, shared rather than built per row: this method runs for every
    // method, of both stages, of all three sections.
    private static readonly Brush SplitRowTaxBrush = CreateFrozenBrush(0x4B, 0x55, 0x63);

    // Lighter than the typed text, so an offered remainder cannot be mistaken for an entered amount.
    private static readonly Brush SplitPlaceholderBrush = CreateFrozenBrush(0x9C, 0xA3, 0xAF);

    private readonly record struct OrderSaveData(
        OrderStatus Status,
        OrderServiceType ServiceType,
        decimal? Subtotal,
        decimal? TaxRate,
        List<OrderItem> ClothingItems,
        string? CustomMadeJson);

    /// <summary>
    /// Every text field that may not be left blank, paired with the message that says so and the block
    /// it belongs under.
    /// </summary>
    /// <remarks>
    /// A list rather than a run of <c>if</c>s so the surfaces cannot drift apart per field — before
    /// this, the phone popped up a dialog, the customer name did not, and neither wrote anything under
    /// its own box. Conditionally-required fields (an address only when shipping, an email only when a
    /// portion settles by e-transfer) stay as their own checks: their rule is about the rest of the
    /// form, not about the box.
    /// </remarks>
    private IEnumerable<RequiredTextField> RequiredTextFields()
    {
        yield return RequiredTextField.For(
            OrderNumberBox, OrderNumberErrorText, _localization["OrderEdit.Validate.OrderNumber"]);

        // Not a TextBox, so it takes the two-closure form the phone field introduced. It belongs in
        // this pass rather than in a check of its own precisely so a form missing both a customer
        // name and a pickup date reports both at once.
        yield return new RequiredTextField(
            () => PickupDatePicker.SelectedDate is null,
            () => PickupDatePicker.Focus(),
            PickupDateErrorText,
            _localization["OrderEdit.Validate.PickupDateRequired"]);

        foreach (var field in CustomerContactFields())
            yield return field;
    }

    /// <summary>
    /// Who the order is for. Required to save, and required before a custom-made record can be
    /// attached — that record belongs to a customer, so it cannot be taken for an unnamed one.
    /// </summary>
    private IEnumerable<RequiredTextField> CustomerContactFields()
    {
        yield return RequiredTextField.For(
            CustomerNameBox, CustomerNameErrorText, _localization["OrderEdit.Validate.CustomerName"]);
        yield return new RequiredTextField(
            () => PhoneField.IsBlank, PhoneField.FocusNumber, PhoneErrorText,
            _localization["OrderEdit.Validate.PhoneNumber"]);
    }

    /// <summary>
    /// One field that may not be left blank: how to ask whether it is, how to put the caret in it, and
    /// what to say.
    /// </summary>
    /// <remarks>
    /// Two closures rather than the <c>TextBox</c> this used to hold, because the phone is no longer a
    /// TextBox — it is a country picker and a number, and "blank" means the number half. Keeping the
    /// TextBox here would have meant lifting the phone out of the one-pass check, and the whole point
    /// of that pass is that two missing fields are reported as two.
    /// </remarks>
    private sealed record RequiredTextField(Func<bool> IsBlank, Action Focus, TextBlock Error, string Message)
    {
        public static RequiredTextField For(TextBox box, TextBlock error, string message)
            => new(() => string.IsNullOrWhiteSpace(box.Text), () => box.Focus(), error, message);
    }

    private IEnumerable<TextBlock> ValidationErrorBlocks()
    {
        yield return OrderNumberErrorText;
        yield return OrderDateErrorText;
        yield return PickupDateErrorText;
        yield return CustomerNameErrorText;
        yield return PhoneErrorText;
        yield return EmailErrorText;
        yield return AddressErrorText;
        yield return StatusReasonCategoryErrorText;
        yield return StatusReasonErrorText;
    }

    private void OnServiceTypeChanged(object sender, RoutedEventArgs e)
    {
        RefreshServicePanels();
        RefreshComputedTotals();
    }

    private void OnAlterationValuesChanged(object sender, TextChangedEventArgs e)
        => RefreshComputedTotals(runAutoComplete: false);

    // The alteration category is normally money-free, but its "None" option switches the whole
    // service off — which changes the totals and the section's locks — so this runs the full
    // refresh. runAutoComplete stays false: choosing a category must never move a payment
    // method selection.
    private void OnServiceCategoryChanged(object sender, SelectionChangedEventArgs e)
        => RefreshComputedTotals(runAutoComplete: false);

    private void OnPaymentOptionChanged(object sender, RoutedEventArgs e)
    {
        // Ignore programmatic changes made while syncing payment state.
        if (_syncingPayment)
            return;

        if (sender is RadioButton radio && FindSectionForRadio(radio) is { } section)
        {
            if (IsFinalMethodRadio(section, radio))
            {
                // The user picked a final-balance method by hand, so it stops following the
                // deposit from here on.
                section.FinalMethodUserChosen = true;
            }
            else
            {
                ApplyDepositMethodChange(section, radio);
            }
        }

        WarnIfEmailMissing(sender);
        UpdatePaymentVisibility();
        RefreshComputedTotals();
    }

    private PaymentSectionControls[] AllSections
        => new[] { _alterationControls, _customMadeControls, _clothingControls };

    // Maps any payment radio back to the section that owns it.
    private PaymentSectionControls? FindSectionForRadio(RadioButton radio)
        => Array.Find(AllSections, c =>
            radio == c.DownNone || radio == c.DownEtransfer || radio == c.DownDebit
            || radio == c.DownCredit || radio == c.DownCash
            || IsFinalMethodRadio(c, radio));

    private static bool IsFinalMethodRadio(PaymentSectionControls c, RadioButton radio)
        => radio == c.FinalEtransfer || radio == c.FinalDebit || radio == c.FinalCredit || radio == c.FinalCash;

    // Selecting a deposit method always clears the deposit-received flag. Only "None" zeroes
    // the deposit amount — switching between real methods (Cash / Card / Etransfer) keeps the
    // entered amount so the breakdown stays accurate.
    //
    // It also re-mirrors the final-balance method, which is the important part: the final
    // method is persisted through EffectiveFinalMethod, so an inherited value comes back from
    // the database looking exactly like a deliberate choice. Without re-mirroring, an order
    // once saved with a card deposit keeps taxing its balance at the card rate even after the
    // deposit is switched to cash. FinalMethodUserChosen is what protects a genuine
    // "deposit by card, balance by cash" override from being overwritten here.
    private void ApplyDepositMethodChange(PaymentSectionControls section, RadioButton radio)
    {
        _syncingPayment = true;
        try
        {
            if (radio == section.DownNone && section.DownpaymentBox.Text != "0")
                section.DownpaymentBox.Text = "0";
            section.DownCompletedCheck.IsChecked = false;

            if (section.FinalMethodUserChosen)
                return;

            var downMethod = GetSelectedDownMethod(section);
            // "None" means no deposit was taken, so there is no method to inherit.
            SetSelectedFinalMethod(section, downMethod == PaymentMethod.None ? null : downMethod);
        }
        finally
        {
            _syncingPayment = false;
        }
    }

    // Recovers, after a load, whether the stored final method was a deliberate override or
    // merely inherited from the deposit. Equal methods are treated as inherited: re-mirroring
    // them is a no-op unless the deposit method actually changes, which is exactly when the
    // final method should follow it.
    private static bool InferFinalMethodWasChosen(PaymentSectionControls c)
    {
        var finalMethod = GetSelectedFinalMethod(c);
        if (finalMethod is null)
            return false;

        return finalMethod != GetSelectedDownMethod(c);
    }

    private CheckBox? GetDownCompletedCheckForBox(TextBox box)
    {
        if (box == AlterationDownpaymentBox)
            return AlterationDownCompletedCheck;
        if (box == CustomMadeDownpaymentBox)
            return CustomMadeDownCompletedCheck;
        if (box == ClothingDownpaymentBox)
            return ClothingDownCompletedCheck;
        return null;
    }

    private void WarnIfEmailMissing(object sender)
    {
        if (_syncingPayment)
            return;

        // Bug 3: only e-transfer needs an email address; Cash/Card don't prompt.
        if (sender is not RadioButton radio || radio.IsChecked is not true)
            return;

        if (!IsEtransferRadio(radio))
            return;

        if (!string.IsNullOrWhiteSpace(EmailBox.Text))
            return;

        // Through the shared path, so choosing e-transfer marks the email box the same way saving
        // without one does. It used to raise a dialog and leave the form looking untouched, which is
        // the case the user is most likely to dismiss and forget.
        ClearValidationErrors();
        Fail("OrderEdit.Validate.EmailRequired", EmailErrorText, EmailBox);
        AnnounceValidationFailure();
    }

    private bool IsEtransferRadio(RadioButton radio)
        => radio == AlterationDownEtransfer || radio == CustomMadeDownEtransfer || radio == ClothingDownEtransfer
            || radio == AlterationFinalEtransfer || radio == CustomMadeFinalEtransfer || radio == ClothingFinalEtransfer;

    private bool HasPaymentMethodRequiringEmail()
    {
        // Bug 3: only e-transfer requires an email address on save.
        return AlterationDownEtransfer.IsChecked is true
            || CustomMadeDownEtransfer.IsChecked is true
            || ClothingDownEtransfer.IsChecked is true
            || AlterationFinalEtransfer.IsChecked is true
            || CustomMadeFinalEtransfer.IsChecked is true
            || ClothingFinalEtransfer.IsChecked is true;
    }

    private void OnAddClothingItemClick(object sender, RoutedEventArgs e)
    {
        AddClothingItemRow();
        // Full refresh, not just RefreshClothingTotals: an item price feeds the order's
        // grand total and payment summary exactly like the other price inputs do.
        // runAutoComplete stays false because editing a price must never move a payment
        // method selection (same rule as the price/tax boxes).
        RefreshComputedTotals(runAutoComplete: false);
    }

    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static",
        Justification = "Named from XAML on four TextBoxes (PreviewTextInput=\"OnDecimalTextBoxPreviewTextInput\"), " +
                        "as well as attached from code. The generated InitializeComponent wires it as " +
                        "this.OnDecimalTextBoxPreviewTextInput, which does not compile against a static method.")]
    private void OnDecimalTextBoxPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        var proposedText = GetProposedText(textBox, e.Text);
        e.Handled = !DecimalInputPattern.IsMatch(proposedText);
    }

    private void SelectServiceType(OrderServiceType serviceType)
    {
        AlterationsRadio.IsChecked = serviceType == OrderServiceType.Alterations;
        CustomMadeRadio.IsChecked = serviceType == OrderServiceType.CustomMade;
        ReadyMadeRadio.IsChecked = serviceType == OrderServiceType.ReadyMade;
        RefreshServicePanels();
    }

    private void RefreshServicePanels()
    {
        // The radio buttons switch which service section is shown, but every section's
        // data is retained in memory and persisted on save, so a single order can carry
        // Alterations, Custom Made and Ready Made charges at the same time.
        var serviceType = GetSelectedServiceType();
        AlterationsPanel.Visibility = serviceType == OrderServiceType.Alterations ? Visibility.Visible : Visibility.Collapsed;
        CustomMadePanel.Visibility = serviceType == OrderServiceType.CustomMade ? Visibility.Visible : Visibility.Collapsed;
        ReadyMadePanel.Visibility = serviceType == OrderServiceType.ReadyMade ? Visibility.Visible : Visibility.Collapsed;
    }

    private OrderServiceType GetSelectedServiceType()
    {
        if (CustomMadeRadio.IsChecked is true)
            return OrderServiceType.CustomMade;

        if (ReadyMadeRadio.IsChecked is true)
            return OrderServiceType.ReadyMade;

        return OrderServiceType.Alterations;
    }

    private void SelectAlterationCategory(string tag)
    {
        foreach (var item in AlterationCategoryBox.Items.OfType<ComboBoxItem>())
            item.IsSelected = string.Equals(item.Tag as string, tag, StringComparison.Ordinal);
    }

    // "None" in the alteration category means this order includes no alteration work at all.
    // The section then takes no charge, contributes nothing to the order total, and its notes
    // and payment inputs are locked so nothing can be entered against a service that is off.
    private bool AlterationServiceSwitchedOff
        => (AlterationCategoryBox.SelectedItem as ComboBoxItem)?.Tag as string == NoAlterationServiceTag;

    // The alteration section's parenthetical: its selected service category.
    private string AlterationDetailText()
    {
        var category = (AlterationCategoryBox.SelectedItem as ComboBoxItem)?.Tag as string;
        return string.IsNullOrWhiteSpace(category) ? string.Empty : LocalizeWithFallback("Alteration.Category", category);
    }

    // The custom-made section's parenthetical: the garments measured across its records,
    // resolved through the same reader the main list's Order.Fields.CustomMadeFlag column uses.
    private string CustomMadeDetailText()
    {
        var languageCode = _localization.CurrentLanguageCode;
        var names = Services.CustomMadeMeasurementReader.GetGarmentNames(_customMadeRecords, languageCode);
        return names.Count == 0 ? string.Empty : _localization.JoinList(names, languageCode);
    }

    // The ready-made section's parenthetical: the distinct item categories priced on it.
    private string ClothingDetailText()
    {
        var names = new List<string>();
        foreach (var row in _clothingItemRows)
        {
            if (GetClothingItemSubtotal(row) <= 0m)
                continue;

            var key = (row.CategoryBox.SelectedItem as ComboBoxItem)?.Tag as string;
            if (string.IsNullOrWhiteSpace(key))
                continue;

            var name = ProductCatalogService.Instance.ResolveName(key);
            if (!names.Contains(name))
                names.Add(name);
        }

        return names.Count == 0 ? string.Empty : _localization.JoinList(names);
    }

    private static System.Windows.Media.SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    // Localizes a token, falling back to the raw token when the key is missing (legacy
    // free-text values predate the fixed category lists).
    private string LocalizeWithFallback(string prefix, string token)
    {
        var key = $"{prefix}.{token}";
        var localized = _localization[key];
        return string.Equals(localized, key, StringComparison.Ordinal) ? token : localized;
    }

    private void UpdatePaymentVisibility()
    {
        var pricesIncludeTax = PricesIncludeTax;
        UpdateSectionVisibility(_alterationControls, pricesIncludeTax);
        UpdateSectionVisibility(_customMadeControls, pricesIncludeTax);
        UpdateSectionVisibility(_clothingControls, pricesIncludeTax);

        ApplySectionLock(_alterationControls);
        ApplySectionLock(_customMadeControls);
        ApplySectionLock(_clothingControls);
    }

    // Once a section's final balance is cleared, its payment is settled and must not be
    // edited, so lock the whole payment section. The "balance cleared" checkbox itself
    // stays enabled (unless the entire order is read-only) so the user can un-clear the
    // section to make it editable again.
    // Additionally, once the deposit is marked received the deposit method radios are
    // frozen — the received payment type must not change after confirmation.
    private void ApplySectionLock(PaymentSectionControls c)
    {
        // IsSettled, not the cleared tick alone: a section with no charge reports cleared
        // because nothing is owed, and disabling its radios here is what makes a zero-priced
        // section stop responding to payment-method clicks after it is reopened.
        var sectionLocked = _isReadOnly || IsSettled(c) || c.IsServiceSwitchedOff;
        // Deposit method radios are frozen once the deposit is marked received OR the whole section is locked.
        var depositMethodLocked = sectionLocked || c.DownCompletedCheck.IsChecked is true;

        c.DownNone.IsEnabled = !depositMethodLocked;
        c.DownEtransfer.IsEnabled = !depositMethodLocked;
        c.DownDebit.IsEnabled = !depositMethodLocked;
        c.DownCredit.IsEnabled = !depositMethodLocked;
        c.DownCash.IsEnabled = !depositMethodLocked;
        // ...and not until a split deposit's rows add up to it: see IsSplitDepositBalanced.
        c.DownCompletedCheck.IsEnabled = !sectionLocked && IsSplitDepositBalanced(c);
        c.FinalEtransfer.IsEnabled = !sectionLocked;
        c.FinalDebit.IsEnabled = !sectionLocked;
        c.FinalCredit.IsEnabled = !sectionLocked;
        c.FinalCash.IsEnabled = !sectionLocked;

        // Assigned unconditionally, both ways. This used to only ever set false, leaving the
        // re-enable to UpdateSectionVisibility — so once anything disabled the box it stayed
        // disabled until that other method happened to run. A lock helper that can only lock is
        // how a control gets stranded. "None" means no deposit is taken, so there is nothing to
        // type either way.
        c.DownpaymentBox.IsEnabled = !sectionLocked && c.DownNone.IsChecked is not true;

        // The split follows the SAME locks as the single-method controls it replaces, which it was
        // not doing at all: a settled section, a read-only order and a received deposit all left their
        // allocation rows and their toggle fully editable. A stage whose money is confirmed must not be
        // re-apportioned behind the confirmation — the tick is the shop saying this is what happened.
        //
        // Per stage, matching the radios above: the DEPOSIT's composition freezes when the deposit is
        // received, the BALANCE's when the section is settled.
        SetSplitStageEnabled(c, finalStage: false, enabled: !depositMethodLocked);
        SetSplitStageEnabled(c, finalStage: true, enabled: !sectionLocked);
    }

    // The pricing mode arrives as an argument rather than being read off the window, so this stays a
    // pure function of the section and the mode — the panel it decides between is chosen by the
    // ORDER's frozen mode, and a saved order must keep the layout it was saved with.
    private static void UpdateSectionVisibility(PaymentSectionControls c, bool pricesIncludeTax)
    {
        var addedAtSettlement = !pricesIncludeTax;
        var depositSplit = addedAtSettlement && c.IsDepositSplit;
        var finalSplit = addedAtSettlement && c.IsFinalSplit;

        ApplySplitModeVisibility(c, addedAtSettlement, depositSplit, finalSplit);
        var stage = ApplyDepositStageState(c);

        // In split mode the method radios are what USED to open this panel, so the split itself opens
        // it — otherwise turning the toggle on would hide the deposit box the split is allocating.
        c.PricingPanel.Visibility = Show(depositSplit || stage.AnyMethodChosen);
        c.FinalBlock.Visibility = Show(stage.IsSkipped || stage.DepositReceived);

        ApplyBreakdownVisibility(c, pricesIncludeTax, depositSplit, finalSplit, stage);
    }

    /// <summary>Visible or collapsed. A helper so a visibility rule reads as the CONDITION it is.</summary>
    /// <remarks>
    /// Eleven inline <c>? Visible : Collapsed</c> ternaries is what carried this method past the
    /// cognitive-complexity limit — each one counts, and none of them said anything. As a call they
    /// cost nothing and the rules line up where they can be compared.
    /// </remarks>
    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Where the section has got to in its deposit: what is chosen, skipped, and received.</summary>
    private readonly record struct DepositStage(bool AnyMethodChosen, bool IsSkipped, bool DepositReceived);

    /// <summary>
    /// Reads the deposit stage, and applies the one rule that CHANGES a control rather than showing
    /// it: skipping the deposit forces the amount to zero and locks the box, because there is nothing
    /// to type when no deposit is being taken.
    /// </summary>
    private static DepositStage ApplyDepositStageState(PaymentSectionControls c)
    {
        var anyMethodChosen = c.DownNone.IsChecked is true || c.DownEtransfer.IsChecked is true
            || c.DownDebit.IsChecked is true || c.DownCredit.IsChecked is true || c.DownCash.IsChecked is true;

        var isSkipped = c.DownNone.IsChecked is true;
        c.DownCompletedCheck.Visibility = Show(!isSkipped);

        if (isSkipped && c.DownpaymentBox.Text != "0")
            c.DownpaymentBox.Text = "0";

        // Skipping the deposit means a deposit of zero, so the split rows have to say zero too.
        // Leaving amounts behind them made the section owe a deposit it had just been told not to take,
        // and the allocation then refused to balance against a target of nothing.
        if (isSkipped)
        {
            foreach (var row in c.DepositRows.Where(row => !row.IsBlank))
                row.Amount.Text = string.Empty;
        }

        c.DownpaymentBox.IsEnabled = !isSkipped;

        return new DepositStage(anyMethodChosen, isSkipped, c.DownCompletedCheck.IsChecked is true);
    }

    /// <summary>
    /// Which of the four breakdowns this section shows: the two that explain tax ADDED at settlement,
    /// the one for a price that already contains it, and the split rows.
    /// </summary>
    /// <remarks>
    /// The deposit breakdown is NEVER shown where the price already contains the tax. Every line it
    /// carries is then either the price restated (a "pre-tax" subtotal that is not pre-tax, a post-tax
    /// total equal to it) or a deposit tax nobody is being asked for — four rows of arithmetic that
    /// always cancels is not a breakdown, it is a puzzle.
    ///
    /// The INCLUSIVE panel follows the final BLOCK rather than the deposit tick: with the deposit
    /// skipped there is nothing to receive and the section goes straight to its balance, so keying it
    /// to the tick would leave such an order showing no figures at all.
    /// </remarks>
    private static void ApplyBreakdownVisibility(
        PaymentSectionControls c, bool pricesIncludeTax, bool depositSplit, bool finalSplit, DepositStage stage)
    {
        var addedAtSettlement = !pricesIncludeTax;

        c.DepositBreakdownPanel.Visibility =
            Show(addedAtSettlement && (depositSplit || stage.AnyMethodChosen) && !stage.DepositReceived);
        // Follows the final BLOCK, not the deposit tick. A skipped deposit opens the final stage
        // without ever ticking anything, and keying the breakdown to the tick left that order showing
        // its balance with nothing explaining it.
        c.FinalBreakdownPanel.Visibility =
            Show(addedAtSettlement && (stage.IsSkipped || stage.DepositReceived));
        c.FinalInclusivePanel.Visibility =
            Show(pricesIncludeTax && (stage.IsSkipped || stage.DepositReceived));

        // The split rows follow their own stage: the deposit's until it is received, the balance's
        // afterwards — the same two stages the rest of the card already moves through. The final rows
        // also open on a SKIPPED deposit, which goes straight to the balance.
        c.DepositSplitPanel.Visibility = Show(depositSplit && !stage.DepositReceived && !stage.IsSkipped);
        c.FinalSplitPanel.Visibility = Show(finalSplit && (stage.IsSkipped || stage.DepositReceived));

        // The toggle is repeated inside the final block so the choice can be made — or changed — at
        // the balance stage without scrolling back to the top of the card. Both pairs drive the one
        // per-section flag; FinalSplitRadio is mirrored from the deposit pair in SyncSplitToggles.
        c.FinalSplitToggle.Visibility = Show(addedAtSettlement && (stage.IsSkipped || stage.DepositReceived));
    }

    private PaymentMethod? GetDownpaymentMethodForServiceType(OrderServiceType serviceType)
        => serviceType switch
        {
            OrderServiceType.Alterations => GetSelectedDownMethod(_alterationControls),
            OrderServiceType.ReadyMade => GetSelectedDownMethod(_clothingControls),
            OrderServiceType.CustomMade => GetSelectedDownMethod(_customMadeControls),
            _ => null
        };

    private PaymentMethod? GetFinalBalanceMethodForServiceType(OrderServiceType serviceType)
        => serviceType switch
        {
            OrderServiceType.Alterations => GetSelectedFinalMethod(_alterationControls),
            OrderServiceType.ReadyMade => GetSelectedFinalMethod(_clothingControls),
            OrderServiceType.CustomMade => GetSelectedFinalMethod(_customMadeControls),
            _ => null
        };

    private void LoadPaymentFields(Order order)
    {
        _syncingPayment = true;
        AlterationDownpaymentBox.Text = order.AlterationDownpayment?.ToString("0.##") ?? string.Empty;
        SetSelectedDownMethod(_alterationControls, order.AlterationDownpaymentMethod);
        AlterationDownCompletedCheck.IsChecked = order.AlterationDownpaymentCompleted;
        SetSelectedFinalMethod(_alterationControls, order.AlterationFinalBalanceMethod);
        AlterationBalanceClearedCheck.IsChecked = order.AlterationBalanceCleared;

        CustomMadeDownpaymentBox.Text = order.CustomMadeDownpayment?.ToString("0.##") ?? string.Empty;
        SetSelectedDownMethod(_customMadeControls, order.CustomMadeDownpaymentMethod);
        CustomMadeDownCompletedCheck.IsChecked = order.CustomMadeDownpaymentCompleted;
        SetSelectedFinalMethod(_customMadeControls, order.CustomMadeFinalBalanceMethod);
        CustomMadeBalanceClearedCheck.IsChecked = order.CustomMadeBalanceCleared;

        ClothingDownpaymentBox.Text = order.ClothingDownpayment?.ToString("0.##") ?? string.Empty;
        SetSelectedDownMethod(_clothingControls, order.ClothingDownpaymentMethod);
        ClothingDownCompletedCheck.IsChecked = order.ClothingDownpaymentCompleted;
        SetSelectedFinalMethod(_clothingControls, order.ClothingFinalBalanceMethod);
        ClothingBalanceClearedCheck.IsChecked = order.ClothingBalanceCleared;

        _syncingPayment = false;

        // The stored final method cannot say whether it was chosen or inherited, so recover
        // that from whether it differs from the deposit's.
        foreach (var section in AllSections)
            section.FinalMethodUserChosen = InferFinalMethodWasChosen(section);

        UpdatePaymentVisibility();
    }

    private void ApplyPaymentFields(Order order)
    {
        // Persist payment details for every section that carries order items — the same test
        // the breakdown and the "clear all balances" pass use. It deliberately is NOT
        // "total > 0": a section can legitimately have items but no charge yet, and gating on
        // money silently threw away its payment method, deposit-received tick and cleared
        // flag on save. Reopening then showed no deposit method selected, which collapses the
        // whole pricing panel and makes the section look broken. A section with no items at
        // all is still cleared out, so an untouched section's default Cash selection never
        // reaches the database.
        // The final-balance method is persisted through EffectiveFinalMethod, the same
        // resolution the on-screen totals use, so the saved order recomputes to exactly the
        // amounts the editor displayed.
        if (_alterationControls.HasItems())
        {
            order.AlterationSubtotal = _alterationSubtotal;
            // Both stage rates are persisted from the section state, not from the tax box —
            // the box only ever holds the stage currently being edited.
            order.AlterationTaxRate = _alterationControls.DepositTaxRate;
            order.AlterationFinalTaxRate = _alterationControls.FinalTaxRate;
            order.AlterationDownpayment = ParseNullableDecimal(AlterationDownpaymentBox.Text);
            order.AlterationDownpaymentMethod = GetSelectedDownMethod(_alterationControls);
            order.AlterationDownpaymentCompleted = AlterationDownCompletedCheck.IsChecked.GetValueOrDefault();
            order.AlterationFinalBalanceMethod = EffectiveFinalMethod(_alterationControls);
            order.AlterationBalanceCleared = AlterationBalanceClearedCheck.IsChecked.GetValueOrDefault();
        }
        else
        {
            order.AlterationSubtotal = null;
            order.AlterationTaxRate = null;
            order.AlterationFinalTaxRate = null;
            ClearSectionPaymentFields(
                value => order.AlterationDownpayment = value,
                method => order.AlterationDownpaymentMethod = method,
                completed => order.AlterationDownpaymentCompleted = completed,
                finalMethod => order.AlterationFinalBalanceMethod = finalMethod,
                cleared => order.AlterationBalanceCleared = cleared);
        }

        if (_customMadeControls.HasItems())
        {
            order.CustomMadeTaxRate = _customMadeControls.DepositTaxRate;
            order.CustomMadeFinalTaxRate = _customMadeControls.FinalTaxRate;
            order.CustomMadeDownpayment = ParseNullableDecimal(CustomMadeDownpaymentBox.Text);
            order.CustomMadeDownpaymentMethod = GetSelectedDownMethod(_customMadeControls);
            order.CustomMadeDownpaymentCompleted = CustomMadeDownCompletedCheck.IsChecked.GetValueOrDefault();
            order.CustomMadeFinalBalanceMethod = EffectiveFinalMethod(_customMadeControls);
            order.CustomMadeBalanceCleared = CustomMadeBalanceClearedCheck.IsChecked.GetValueOrDefault();
        }
        else
        {
            order.CustomMadeTaxRate = null;
            order.CustomMadeFinalTaxRate = null;
            ClearSectionPaymentFields(
                value => order.CustomMadeDownpayment = value,
                method => order.CustomMadeDownpaymentMethod = method,
                completed => order.CustomMadeDownpaymentCompleted = completed,
                finalMethod => order.CustomMadeFinalBalanceMethod = finalMethod,
                cleared => order.CustomMadeBalanceCleared = cleared);
        }

        if (_clothingControls.HasItems())
        {
            order.ClothingSubtotal = _clothingSubtotal;
            order.ClothingTaxRate = _clothingControls.DepositTaxRate;
            order.ClothingFinalTaxRate = _clothingControls.FinalTaxRate;
            order.ClothingDownpayment = ParseNullableDecimal(ClothingDownpaymentBox.Text);
            order.ClothingDownpaymentMethod = GetSelectedDownMethod(_clothingControls);
            order.ClothingDownpaymentCompleted = ClothingDownCompletedCheck.IsChecked.GetValueOrDefault();
            order.ClothingFinalBalanceMethod = EffectiveFinalMethod(_clothingControls);
            order.ClothingBalanceCleared = ClothingBalanceClearedCheck.IsChecked.GetValueOrDefault();
        }
        else
        {
            order.ClothingSubtotal = null;
            order.ClothingTaxRate = null;
            order.ClothingFinalTaxRate = null;
            ClearSectionPaymentFields(
                value => order.ClothingDownpayment = value,
                method => order.ClothingDownpaymentMethod = method,
                completed => order.ClothingDownpaymentCompleted = completed,
                finalMethod => order.ClothingFinalBalanceMethod = finalMethod,
                cleared => order.ClothingBalanceCleared = cleared);
        }

        // Aggregate downpayment across all three services (Requirement 3a).
        order.Downpayment = order.TotalDownpayment;

        // Backward-compatible single-method fields reflect the active service selection.
        var activeServiceType = GetSelectedServiceType();
        order.DownpaymentMethod = GetDownpaymentMethodForServiceType(activeServiceType);
        order.FinalBalanceMethod = GetFinalBalanceMethodForServiceType(activeServiceType);
    }

    private static void ClearSectionPaymentFields(
        Action<decimal?> setDownpayment,
        Action<PaymentMethod?> setDownpaymentMethod,
        Action<bool> setDownpaymentCompleted,
        Action<PaymentMethod?> setFinalBalanceMethod,
        Action<bool> setBalanceCleared)
    {
        setDownpayment(null);
        setDownpaymentMethod(null);
        setDownpaymentCompleted(false);
        setFinalBalanceMethod(null);
        setBalanceCleared(false);
    }

    // These four take the whole section rather than a positional list of radios: with debit and
    // credit split out there are five deposit radios and four final ones, and a call site that
    // passed them in the wrong order would compile and then silently read the wrong method.
    private static PaymentMethod? GetSelectedDownMethod(PaymentSectionControls c)
    {
        if (c.DownNone.IsChecked is true)
            return PaymentMethod.None;
        if (c.DownEtransfer.IsChecked is true)
            return PaymentMethod.Etransfer;
        if (c.DownDebit.IsChecked is true)
            return PaymentMethod.DebitCard;
        if (c.DownCredit.IsChecked is true)
            return PaymentMethod.CreditCard;
        if (c.DownCash.IsChecked is true)
            return PaymentMethod.Cash;
        return null;
    }

    private static void SetSelectedDownMethod(PaymentSectionControls c, PaymentMethod? method)
    {
        // An order saved before the split recorded the single "Card" value; it resolves to debit,
        // which is what that option's label named. Without this the radios would all come back
        // unchecked and UpdateSectionVisibility would collapse the section's whole pricing panel.
        var resolved = method is null ? null : (PaymentMethod?)PaymentTaxRules.Normalize(method.Value);

        c.DownNone.IsChecked = resolved == PaymentMethod.None;
        c.DownEtransfer.IsChecked = resolved == PaymentMethod.Etransfer;
        c.DownDebit.IsChecked = resolved == PaymentMethod.DebitCard;
        c.DownCredit.IsChecked = resolved == PaymentMethod.CreditCard;
        c.DownCash.IsChecked = resolved == PaymentMethod.Cash;
    }

    private static PaymentMethod? GetSelectedFinalMethod(PaymentSectionControls c)
    {
        if (c.FinalEtransfer.IsChecked is true)
            return PaymentMethod.Etransfer;
        if (c.FinalDebit.IsChecked is true)
            return PaymentMethod.DebitCard;
        if (c.FinalCredit.IsChecked is true)
            return PaymentMethod.CreditCard;
        if (c.FinalCash.IsChecked is true)
            return PaymentMethod.Cash;
        return null;
    }

    private static void SetSelectedFinalMethod(PaymentSectionControls c, PaymentMethod? method)
    {
        var resolved = method is null ? null : (PaymentMethod?)PaymentTaxRules.Normalize(method.Value);

        c.FinalEtransfer.IsChecked = resolved == PaymentMethod.Etransfer;
        c.FinalDebit.IsChecked = resolved == PaymentMethod.DebitCard;
        c.FinalCredit.IsChecked = resolved == PaymentMethod.CreditCard;
        c.FinalCash.IsChecked = resolved == PaymentMethod.Cash;
    }

    /// <summary>
    /// Re-adds a category an order refers to but the shop's catalogue no longer offers, so editing
    /// an old order does not silently change what it says it sold.
    /// </summary>
    private static ComboBoxItem AddOrphanedCategory(ComboBox categoryBox, string productName)
    {
        var item = new ComboBoxItem
        {
            Content = ProductCatalogService.Instance.ResolveName(productName),
            Tag = productName
        };

        categoryBox.Items.Add(item);
        return item;
    }

    private void AddClothingItemRow(OrderItem? existingItem = null)
    {
        var rowGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
            SharedSizeGroup = ClothingRowActionsGroup
        });

        var categoryBox = new ComboBox { Margin = new Thickness(0, 0, 10, 0), Padding = new Thickness(6, 4, 6, 4) };

        // From the SHOP's catalogue, not a fixed list: Local Configuration → Product Categories edits it per branch.
        var categories = ProductCatalogService.Instance.Items
            .Select(item => new ComboBoxItem
            {
                Content = ProductCatalogService.Instance.ResolveName(item.Id),
                Tag = item.Id
            });

        foreach (var category in categories)
            categoryBox.Items.Add(category);

        categoryBox.SelectedIndex = 0;
        if (!string.IsNullOrWhiteSpace(existingItem?.ProductName))
        {
            // A search, not a loop: take the first category whose tag matches, and leave the
            // SelectedIndex = 0 set above in place when nothing does.
            var match = categoryBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(
                    item.Tag?.ToString(), existingItem.ProductName, StringComparison.OrdinalIgnoreCase));

            // An order can name a category this shop has since removed from its catalogue. Rather
            // than silently re-filing it under whatever sits at index 0, the original is added back
            // as a one-off entry so opening an old order does not quietly rewrite it.
            match ??= AddOrphanedCategory(categoryBox, existingItem.ProductName);

            if (match is not null)
                categoryBox.SelectedItem = match;
        }
        Grid.SetColumn(categoryBox, 0);

        var unitPriceBox = new TextBox
        {
            Margin = new Thickness(0, 0, 10, 0),
            Padding = new Thickness(6, 4, 6, 4),
            Text = existingItem?.UnitPrice.ToString("0.##") ?? string.Empty
        };
        RegisterMoneyBox(unitPriceBox);
        unitPriceBox.PreviewTextInput += OnDecimalTextBoxPreviewTextInput;
        Grid.SetColumn(unitPriceBox, 1);

        var promotionalPriceBox = new TextBox
        {
            Margin = new Thickness(0, 0, 10, 0),
            Padding = new Thickness(6, 4, 6, 4),
            Text = existingItem?.PromotionalPrice?.ToString("0.##") ?? string.Empty
        };
        // Optional field: a blank promotional price means "no promotion", so it must stay blank.
        RegisterMoneyBox(promotionalPriceBox, restoreZeroOnBlur: false);
        promotionalPriceBox.PreviewTextInput += OnDecimalTextBoxPreviewTextInput;
        Grid.SetColumn(promotionalPriceBox, 2);

        var subtotalText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold
        };
        Grid.SetColumn(subtotalText, 3);

        var removeButton = new Button
        {
            Content = _localization["OrderEdit.RemoveItem"],
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(10, 4, 10, 4)
        };
        Grid.SetColumn(removeButton, 4);

        var row = new ClothingItemEditorRow(rowGrid, categoryBox, unitPriceBox, promotionalPriceBox, subtotalText, removeButton);

        unitPriceBox.TextChanged += (_, _) => RefreshComputedTotals(runAutoComplete: false);
        promotionalPriceBox.TextChanged += (_, _) => RefreshComputedTotals(runAutoComplete: false);
        // The item category is money-free but names the ready-made line in the breakdown
        // under the order total, so it has to refresh that.
        categoryBox.SelectionChanged += (_, _) => RefreshServicesTotalBreakdown();
        removeButton.Click += (_, _) =>
        {
            ClothingItemsPanel.Children.Remove(row.Container);
            _clothingItemRows.Remove(row);
            RefreshComputedTotals(runAutoComplete: false);
        };

        rowGrid.Children.Add(categoryBox);
        rowGrid.Children.Add(unitPriceBox);
        rowGrid.Children.Add(promotionalPriceBox);
        rowGrid.Children.Add(subtotalText);
        rowGrid.Children.Add(removeButton);

        if (ClothingItemsPanel.Children.Count == 0)
            ClothingItemsPanel.Children.Add(CreateClothingHeader());

        ClothingItemsPanel.Children.Add(rowGrid);
        _clothingItemRows.Add(row);

        if (_isReadOnly)
            ApplyReadOnlyModeToClothingRows();
    }

    /// <summary>
    /// Ties the header's trailing column to the item rows' Remove-button column, so the two Grids
    /// divide the same space and the headings line up with the values under them.
    /// </summary>
    private const string ClothingRowActionsGroup = "ClothingRowActions";

    private UIElement CreateClothingHeader()
    {
        var headerGrid = new Grid { Margin = new Thickness(0, 12, 0, 8) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        // Shares its width with the item rows' Remove-button column — see ClothingItemsPanel in the
        // markup. Empty here, and without the shared group it measured 0, which pushed every heading
        // right of the values beneath it.
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
            SharedSizeGroup = ClothingRowActionsGroup
        });

        headerGrid.Children.Add(CreateHeaderText(_localization["Order.Fields.ItemCategory"], 0));
        headerGrid.Children.Add(CreateHeaderText(_localization["Order.Fields.UnitPrice"], 1));
        headerGrid.Children.Add(CreateHeaderText(_localization["Order.Fields.PromotionalPrice"], 2));
        headerGrid.Children.Add(CreateHeaderText(_localization["Order.Fields.Subtotal"], 3));
        return headerGrid;
    }

    private static TextBlock CreateHeaderText(string text, int column)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Foreground = System.Windows.Media.Brushes.DimGray
        };
        Grid.SetColumn(textBlock, column);
        return textBlock;
    }

    private List<OrderItem> BuildClothingItems()
    {
        var items = new List<OrderItem>();
        foreach (var row in _clothingItemRows)
        {
            var unitPrice = ParseDecimalOrZero(row.UnitPriceBox.Text);
            var promotionalPrice = ParseNullableDecimal(row.PromotionalPriceBox.Text);
            var selectedCategory = (row.CategoryBox.SelectedItem as ComboBoxItem)?.Tag?.ToString()
                ?? ProductCatalogService.Instance.Items.FirstOrDefault()?.Id
                ?? string.Empty;

            if (unitPrice <= 0m && (!promotionalPrice.HasValue || promotionalPrice.Value <= 0m))
                continue;

            items.Add(new OrderItem
            {
                ProductName = selectedCategory,
                Quantity = 1,
                UnitPrice = unitPrice,
                PromotionalPrice = promotionalPrice > 0m ? promotionalPrice : null
            });
        }

        return items;
    }

    private static decimal GetClothingItemSubtotal(ClothingItemEditorRow row)
    {
        var unitPrice = ParseDecimalOrZero(row.UnitPriceBox.Text);
        var promotionalPrice = ParseNullableDecimal(row.PromotionalPriceBox.Text);
        return promotionalPrice.HasValue && promotionalPrice.Value > 0m ? promotionalPrice.Value : unitPrice;
    }

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static decimal ParseDecimalOrZero(string? value)
        => decimal.TryParse(value, out var result) ? result : 0m;

    /// <summary>
    /// Formats an amount in the currency THIS FORM is pricing in — the picker's value, not the shop's
    /// setting. An instance method for that reason: the currency is a property of the order being
    /// edited, so a static helper could only ever have answered for the shop.
    /// </summary>
    private string FormatCurrency(decimal amount)
        => Services.CurrencySettingService.Format(amount, SelectedCurrency, grouped: false);

    private static decimal? ParseNullableDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return decimal.TryParse(value, out var result) ? result : null;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private sealed record ClothingItemEditorRow(
        Grid Container,
        ComboBox CategoryBox,
        TextBox UnitPriceBox,
        TextBox PromotionalPriceBox,
        TextBlock SubtotalText,
        Button RemoveButton);
}
