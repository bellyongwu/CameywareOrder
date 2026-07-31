using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        _isReadOnly = IsReadOnlyStatus(existing.Status);
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

    private void ApplyReadOnlyMode()
    {
        SaveButton.Visibility = Visibility.Collapsed;
        ReadOnlyNotice.Visibility = Visibility.Visible;

        StatusBox.IsEnabled = false;
        PickedUpCheck.IsEnabled = false;
        ClearAllBalancesCheck.IsEnabled = false;

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
        SelectServiceType(OrderServiceType.Alterations);
        RefreshLocalizedLabels();
    }

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

        RefreshCustomMadeButtonLabel();

        RefreshServicePanels();
        RefreshCustomMadeEmptyState();
        RefreshPaymentLabels();
        UpdateStatusReasonVisibility();
        RefreshComputedTotals();
    }

    // The record button opens the custom-made editor in view mode when the whole
    // order is read-only OR the custom-made section balance is cleared (settled),
    // so its label mirrors that state (View vs. Edit).
    private void RefreshCustomMadeButtonLabel()
    {
        var viewOnly = _isReadOnly || IsSettled(_customMadeControls);
        EditCustomMadeButton.Content = _localization[viewOnly ? "OrderEdit.ViewCustomMade" : "OrderEdit.EditCustomMade"];
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

    // ── Splitting a stage across payment types ───────────────────────────────────────────────────

    /// <summary>
    /// Builds one amount row per configurable payment method, for both stages of every section.
    /// </summary>
    /// <remarks>
    /// Driven from <c>PaymentTaxRules.ConfigurableMethods</c>, so the rows are exactly the methods the
    /// shop can configure and adding one needs no change here or in the markup. The legacy
    /// <c>PaymentMethod.Card</c> is not among them, and "None" is the absence of a payment rather than
    /// a way of paying — in a split it is expressed by leaving every box empty.
    /// </remarks>
    private void BuildSplitRows()
    {
        foreach (var section in AllPaymentSections)
        {
            Fill(section.DepositSplitRows, section.DepositRows);
            Fill(section.FinalSplitRows, section.FinalRows);

            // The default lives HERE rather than as IsChecked in the markup: set there it fires the
            // Checked handler during InitializeComponent, against controls that do not exist yet.
            //
            // BOTH pairs. The balance stage's copy was left unset, so that toggle opened with neither
            // option chosen — the card said nothing about how the balance would be taken until somebody
            // clicked one. "No split" is the answer every section starts from, at either stage.
            section.NoSplitRadio.IsChecked = true;
            section.FinalNoSplitRadio.IsChecked = true;
        }

        // Everything the payment handlers touch now exists.
        _sectionsReady = true;

        void Fill(Panel host, List<SplitRow> rows)
        {
            host.Children.Clear();
            rows.Clear();

            foreach (var method in PaymentTaxRules.ConfigurableMethods)
            {
                var grid = new Grid { Margin = new Thickness(0, 0, 14, 6) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });

                var label = new TextBlock
                {
                    Text = _localization[$"PaymentMethod.{method}"],
                    VerticalAlignment = VerticalAlignment.Center,
                };

                var amount = new TextBox { Padding = new Thickness(6, 4, 6, 4) };
                amount.PreviewTextInput += OnDecimalTextBoxPreviewTextInput;
                amount.TextChanged += OnSplitAmountChanged;
                amount.GotKeyboardFocus += OnSplitAmountFocused;
                amount.LostFocus += OnSplitAmountCommitted;

                // The placeholder sits BEHIND the box in the same cell, which is how the status-reason
                // field already does it: a TextBox has no placeholder of its own, and a hint drawn
                // beside the box would read as a value somebody had typed.
                var placeholder = new TextBlock
                {
                    Margin = new Thickness(7, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = SplitPlaceholderBrush,
                    IsHitTestVisible = false,
                };

                var field = new Grid { Margin = new Thickness(0, 0, 12, 0) };
                field.Children.Add(amount);
                field.Children.Add(placeholder);
                Grid.SetColumn(field, 1);

                var detail = new TextBlock
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = SplitRowTaxBrush,
                };
                Grid.SetColumn(detail, 2);

                grid.Children.Add(label);
                grid.Children.Add(field);
                grid.Children.Add(detail);
                host.Children.Add(grid);

                rows.Add(new SplitRow(method, amount, detail, placeholder));
            }
        }
    }

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

    /// <summary>What a stage's split still has to account for: its target, less what is allocated.</summary>
    private static decimal SplitShortfall(PaymentSectionControls c, bool finalStage, decimal target)
        => target - (finalStage ? c.FinalRows : c.DepositRows).Sum(row => row.Value);

    /// <summary>
    /// Whether a split deposit's rows add up to the deposit, which is what lets it be marked received.
    /// </summary>
    /// <remarks>
    /// Ticking "received" is the shop saying the money is in hand and moving on: the deposit rows
    /// disappear and the balance stage opens. Allowing that over an allocation that does not balance
    /// stores a deposit whose payment types add up to something else, and the stage it came from is no
    /// longer on screen to correct it. Refusing at SAVE was not enough — by then the evidence is gone.
    ///
    /// Always true for a section that is not split, and where the price already contains the tax:
    /// there is nothing to balance in either case.
    ///
    /// Consulted from <c>ApplySectionLock</c> rather than assigned from the refresh pass. The checkbox's
    /// enabled state has ONE owner, and that method assigns it unconditionally — a gate written
    /// anywhere else is simply overwritten a moment later, which is what the first attempt did.
    /// </remarks>
    private bool IsSplitDepositBalanced(PaymentSectionControls c)
    {
        if (!c.IsDepositSplit || PricesIncludeTax)
            return true;

        return SplitShortfall(c, finalStage: false, SectionMoney(c).Deposit) == 0m;
    }

    /// <summary>
    /// Every split stage must account for exactly what that stage owes, or the order is refused.
    /// </summary>
    /// <remarks>
    /// A shortfall is a PARTIAL payment, and there is no such state anywhere in this application — not
    /// on the order, not on the receipt, not in the balance column — so accepting one would store a
    /// number no screen could explain. An over-allocation is refused for the same reason from the other
    /// side: money taken that the section does not owe.
    ///
    /// Only the stage that is CURRENTLY on screen is checked. The final stage's rows are not visible,
    /// and cannot have been filled in, until the deposit is marked received — holding a shop to an
    /// allocation of a balance it has not reached yet would make the deposit unsaveable.
    /// </remarks>
    private bool ValidateSplitAllocations()
    {
        foreach (var c in AllPaymentSections)
        {
            if (PricesIncludeTax || c.IsServiceSwitchedOff)
                continue;

            var money = SectionMoney(c);
            var finalStage = c.DownCompletedCheck.IsChecked is true;

            // Only the stage that is on screen, and only if THAT stage is the split one — the deposit
            // and the balance answer separately now.
            if (!c.IsSplitAt(finalStage))
                continue;

            var target = finalStage ? money.FinalBase : money.Deposit;
            var shortfall = SplitShortfall(c, finalStage, target);

            if (shortfall == 0m)
                continue;

            // Short and over are different problems and need different sentences. One message with an
            // absolute value told a shop that had allocated 1200 against 600 that "600 is not allocated
            // to a payment type", which is the opposite of what happened.
            var message = _localization.Format(
                shortfall > 0m ? "OrderEdit.Validate.SplitUnbalanced" : "OrderEdit.Validate.SplitOverpaid",
                _localization[c.ServiceNameKey], FormatCurrency(Math.Abs(shortfall)));

            RecordValidationFailure(new[] { message });
            (finalStage ? c.FinalRows : c.DepositRows)[0].Amount.Focus();
            return false;
        }

        return true;
    }

    /// <summary>The money split a section is currently showing, for a check that runs outside a refresh.</summary>
    private SectionPayment SectionMoney(PaymentSectionControls c)
    {
        if (ReferenceEquals(c, _alterationControls))
            return _alterationMoney;

        return ReferenceEquals(c, _customMadeControls) ? _customMadeMoney : _clothingMoney;
    }

    /// <summary>Freezes each section's split onto the order at save.</summary>
    /// <remarks>
    /// Written for EVERY section, including the ones with the toggle off — <c>SetPaymentSplits</c>
    /// stores null when nothing is split, so an order that has never used the feature keeps an empty
    /// column rather than carrying three empty objects around.
    /// </remarks>
    private void ApplyPaymentSplits(Order order)
    {
        var splits = new OrderPaymentSplits();

        Capture(OrderPaymentSplits.AlterationKey, _alterationControls);
        Capture(OrderPaymentSplits.CustomMadeKey, _customMadeControls);
        Capture(OrderPaymentSplits.ClothingKey, _clothingControls);

        order.SetPaymentSplits(splits);

        void Capture(string key, PaymentSectionControls c)
        {
            var section = splits.For(key);
            section.DepositEnabled = c.IsDepositSplit;
            section.FinalEnabled = c.IsFinalSplit;
            section.Deposit = ReadSplitLines(c, finalStage: false).ToList();
            section.Final = ReadSplitLines(c, finalStage: true).ToList();
        }
    }

    /// <summary>Puts a saved order's splits back on screen: the toggle, then each method's amount.</summary>
    /// <remarks>
    /// Under the payment guard, like every other control this window fills from a saved order: setting
    /// a radio or a text box raises the handlers that recompute the totals, and doing that while the
    /// rest of the form is still being populated reads half a form.
    /// </remarks>
    private void LoadPaymentSplits(Order order)
    {
        var splits = order.PaymentSplits;

        Restore(OrderPaymentSplits.AlterationKey, _alterationControls);
        Restore(OrderPaymentSplits.CustomMadeKey, _customMadeControls);
        Restore(OrderPaymentSplits.ClothingKey, _clothingControls);

        void Restore(string key, PaymentSectionControls c)
        {
            var section = splits.For(key);
            c.SplitRadio.IsChecked = section.DepositEnabled;
            c.NoSplitRadio.IsChecked = !section.DepositEnabled;
            c.FinalSplitRadio.IsChecked = section.FinalEnabled;
            c.FinalNoSplitRadio.IsChecked = !section.FinalEnabled;

            Fill(c.DepositRows, section.Deposit);
            Fill(c.FinalRows, section.Final);
        }

        static void Fill(List<SplitRow> rows, List<PaymentSplitLine> lines)
        {
            foreach (var row in rows)
            {
                var line = lines.Find(l => l.Method == row.Method);
                row.Amount.Text = line is { Amount: > 0m } ? line.Amount.ToString("0.##") : string.Empty;
            }
        }
    }

    /// <summary>Turning the split on or off re-shapes the card, so everything is recomputed.</summary>
    /// <remarks>
    /// Serves BOTH stages' toggles — the deposit's and the balance's — because recomputing is all
    /// either one has ever done. The stages still answer independently: which of them is split lives
    /// in <c>SectionPaymentSplit.DepositEnabled</c> / <c>FinalEnabled</c>, which
    /// <see cref="RefreshComputedTotals"/> reads back per stage. They were once mirrored, so picking
    /// "split" for a balance re-shaped a deposit that had already been taken; what fixed that was
    /// separating the DATA, not having two handlers with the same body.
    ///
    /// Guarded on <see cref="_sectionsReady"/>, not only on the payment sync flag. A RadioButton whose
    /// <c>IsChecked</c> is set in MARKUP raises Checked while <c>InitializeComponent</c> is still
    /// running — before any of the section controls exist — so the first thing this handler did was
    /// dereference a null and take the whole window down on open. The markup default was removed as
    /// well, and this guard is what stops the next one from doing it again.
    /// </remarks>
    private void OnSplitModeChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingPayment || !_sectionsReady)
            return;

        RefreshComputedTotals();
    }

    /// <summary>False until the section controls are built, so parse-time events cannot reach them.</summary>
    private bool _sectionsReady;

    /// <summary>
    /// A typed amount changes the tax, the totals and the allocation line, so it goes through the same
    /// refresh every other payment input does — never a local update, which is how two figures on one
    /// card come to disagree.
    /// </summary>
    private void OnSplitAmountChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingPayment)
            return;

        RefreshComputedTotals();
    }

    /// <summary>
    /// Writes each row's tax and the stage's allocation line: what is allocated against what is owed,
    /// and what is left.
    /// </summary>
    /// <remarks>
    /// The per-row tax is computed the way the money calculation computes it — the shop's CURRENT rules
    /// decide whether a method is taxed at all, its stored rate decides how much — so a row showing
    /// "0.00" beside a card is telling the truth about a shop that has made cards tax free, rather than
    /// disagreeing with the total underneath.
    /// </remarks>
    private void RefreshSplitStage(PaymentSectionControls c, bool finalStage, decimal target)
    {
        var rows = finalStage ? c.FinalRows : c.DepositRows;
        var summary = finalStage ? c.FinalSplitSummary : c.DepositSplitSummary;
        var rules = PaymentTaxRules.Active;

        var allocated = 0m;
        var tax = 0m;

        foreach (var row in rows)
        {
            var amount = row.Value;
            allocated += amount;

            var rate = rules.IsTaxable(row.Method) ? rules.RateFor(row.Method) : 0m;
            // Rounded exactly as PortionTax rounds it, per line and before summing. This figure is
            // shown to the shop and that one is charged to the customer; they have to be one number.
            var rowTax = MoneyRounding.Round(amount * rate / 100m);
            tax += rowTax;

            // Each line says what it costs the customer, not just its tax: the amount typed is
            // pre-tax, so the figure they are actually asked for at the till is amount + tax and
            // nothing else on the card states it per method.
            row.Detail.Text = amount > 0m
                ? _localization.Format("OrderEdit.Split.RowDetail",
                    FormatTaxRate(rate), FormatCurrency(rowTax), FormatCurrency(amount + rowTax))
                : string.Empty;
        }

        var left = target - allocated;
        ShowRemainderPlaceholders(rows, left);

        // The line above already states the allocation against the target, so this one says what is
        // WRONG: how much is missing, or how much too much. Naming the ceiling again read as a rule
        // rather than as the thing to correct.
        var state = left switch
        {
            > 0m => _localization.Format("OrderEdit.Split.Remaining", FormatCurrency(left)),
            < 0m => _localization.Format("OrderEdit.Split.Overpaid", FormatCurrency(-left)),
            _ => string.Empty,
        };

        summary.Text = _localization.Format("OrderEdit.Split.Summary",
            FormatCurrency(allocated), FormatCurrency(target), FormatCurrency(tax));

        if (state.Length > 0)
            summary.Text += Environment.NewLine + state;

        summary.Foreground = left == 0m ? BalancedSplitBrush : UnbalancedSplitBrush;
    }

    /// <summary>
    /// Offers what is still unallocated as a placeholder in every row that has not been answered yet.
    /// </summary>
    /// <remarks>
    /// A hint, not a value: nothing is charged until somebody puts it in the box. Recomputed on every
    /// keystroke, so the offer is always the target LESS what has been entered — change one row from
    /// 400 to 300 and every empty row is offering 100 before the next character can be typed.
    ///
    /// It disappears once the stage balances, and never appears on an over-allocated stage, where the
    /// honest next move is to take an amount OUT rather than to be offered more.
    /// </remarks>
    private void ShowRemainderPlaceholders(List<SplitRow> rows, decimal left)
    {
        foreach (var row in rows)
        {
            row.Placeholder.Text = row.IsBlank && left > 0m ? FormatCurrency(left) : string.Empty;
            row.Placeholder.Visibility = Show(row.IsBlank);
        }
    }

    /// <summary>
    /// Clicking into an empty row fills it with everything still unallocated.
    /// </summary>
    /// <remarks>
    /// The figure it writes is ordinary editable text, and the rows it does NOT touch are every other
    /// one: a row already carrying an amount is an answer, and a row still empty keeps offering
    /// whatever is left. Typing 300 over an offered 400 therefore leaves 100, which the remaining empty
    /// rows immediately offer in turn — the allocation walks down the list as the shop fills it in.
    ///
    /// An earlier version settled the other empty rows at zero, to balance the stage in one click. That
    /// was wrong in the way that matters: a typed zero is an ANSWER ("nothing was taken this way"), so
    /// writing it on the shop's behalf both stated something nobody had said and stopped those rows
    /// from ever offering the remainder again.
    /// </remarks>
    private void OnSplitAmountFocused(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_syncingPayment || !_sectionsReady || sender is not TextBox box)
            return;

        if (FindSplitSlot(box) is not { } slot || !slot.Row.IsBlank)
            return;

        var left = SplitTargetFor(slot.Section, slot.FinalStage) - slot.Rows.Sum(row => row.Value);
        if (left <= 0m)
            return;

        _syncingPayment = true;
        try
        {
            slot.Row.Amount.Text = left.ToString("0.##", CultureInfo.CurrentCulture);
        }
        finally
        {
            _syncingPayment = false;
        }

        RefreshComputedTotals();
    }

    /// <summary>
    /// Leaving a row that has taken the stage over its target pulls it back to the largest amount that
    /// still fits.
    /// </summary>
    /// <remarks>
    /// The row CORRECTED is the one just edited, which is the one the shop meant to change — the others
    /// are amounts already agreed, and moving those to make room would silently rewrite a payment that
    /// had been recorded correctly.
    ///
    /// On losing focus rather than on each keystroke. Clamping as the digits arrive fights the typist:
    /// "900" against a 400 ceiling would be rewritten at "9" and never reach a second character. While
    /// typing, the summary says what is wrong; leaving the field is what accepts the correction.
    /// </remarks>
    private void OnSplitAmountCommitted(object sender, RoutedEventArgs e)
    {
        if (_syncingPayment || !_sectionsReady || sender is not TextBox box)
            return;

        if (FindSplitSlot(box) is not { } slot)
            return;

        var others = slot.Rows.Where(row => !ReferenceEquals(row, slot.Row)).Sum(row => row.Value);
        var room = SplitTargetFor(slot.Section, slot.FinalStage) - others;

        if (slot.Row.Value <= room)
            return;

        _syncingPayment = true;
        try
        {
            // Never below zero: rows already agreed can add up past the target on their own, and the
            // honest answer for this one is then "nothing left for you".
            slot.Row.Amount.Text = Math.Max(room, 0m).ToString("0.##", CultureInfo.CurrentCulture);
        }
        finally
        {
            _syncingPayment = false;
        }

        RefreshComputedTotals();
    }

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

    /// <summary>What a stage's rows must add up to: the deposit typed in, or the balance left after it.</summary>
    private decimal SplitTargetFor(PaymentSectionControls c, bool finalStage)
    {
        var money = SectionMoney(c);
        return finalStage ? money.FinalBase : money.Deposit;
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

    private void InitializeCustomMadeRecordsList()
    {
        CustomMadeRecordsList.ItemsSource = _customMadeRecords;
    }

    private void LoadCustomMadeRecords(string? json)
    {
        _customMadeRecords.Clear();

        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var records = JsonSerializer.Deserialize<List<CustomMadeServiceRecord>>(json) ?? new List<CustomMadeServiceRecord>();
                foreach (var record in records)
                    _customMadeRecords.Add(record);
            }
            catch
            {
                // Ignore malformed legacy payloads and start with an empty list.
            }
        }

        RefreshCustomMadeEmptyState();
    }

    private void RefreshCustomMadeEmptyState()
    {
        CustomMadeEmptyText.Visibility = _customMadeRecords.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private readonly record struct OrderSaveData(
        OrderStatus Status,
        OrderServiceType ServiceType,
        decimal? Subtotal,
        decimal? TaxRate,
        List<OrderItem> ClothingItems,
        string? CustomMadeJson);

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;

        if (!TryValidateForSave(out var status))
            return;

        var serviceType = GetSelectedServiceType();
        var data = new OrderSaveData(
            status,
            serviceType,
            GetSubtotalForServiceType(serviceType),
            GetTaxRateForServiceType(serviceType),
            // Every section is persisted independently, so clothing items are always captured.
            BuildClothingItems(),
            _customMadeRecords.Count == 0 ? null : JsonSerializer.Serialize(_customMadeRecords));

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            string? newOrderNumber = null;
            if (_existing is null)
                newOrderNumber = AddNewOrder(db, data);
            else
                await UpdateExistingOrderAsync(db, data);

            await db.SaveChangesAsync();

            // Only after the order is safely written: the shop's receipt counter must never move
            // for an order that failed to save, or the run would show a gap nobody can account for.
            if (newOrderNumber is not null)
                AdvanceShopReceiptCounter(newOrderNumber);

            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = _localization.Format("OrderEdit.SaveFailed", ex.Message);
        }
    }

    /// <summary>
    /// Validates, marks every problem in place, and raises ONE dialog if anything is wrong.
    /// </summary>
    /// <remarks>
    /// The dialog lives here and nowhere else, which is what makes the rest of validation testable: a
    /// <c>MessageBox</c> reached from inside a check blocks the thread, so a harness driving Save with
    /// a blank field would hang on a dialog nothing can answer. <see cref="ValidateForSave"/> marks
    /// without announcing; this announces what was marked. It also means one dialog listing every
    /// problem rather than one dialog per field.
    /// </remarks>
    private bool TryValidateForSave(out OrderStatus status)
    {
        if (ValidateForSave(out status))
            return true;

        AnnounceValidationFailure();
        return false;
    }

    /// <summary>The testable half: validates and MARKS the form, and never opens a dialog.</summary>
    private bool ValidateForSave(out OrderStatus status)
    {
        status = default;
        ClearValidationErrors();

        if (!TryRequireFilled(RequiredTextFields()))
            return false;

        // Present but malformed. Both already write their own inline message.
        // ValidatePhoneField has already written the inline message, which names the country and the
        // digits it expects; the banner takes the general one so it stays one line per problem.
        if (!ValidatePhoneField())
        {
            PhoneField.FocusNumber();
            return Fail("OrderEdit.Validate.PhoneInvalid", null, null);
        }

        if (!ValidateEmailField())
            return Fail("OrderEdit.Validate.EmailInvalid", null, EmailBox);

        RefreshComputedTotals();

        if (HasPaymentMethodRequiringEmail() && string.IsNullOrWhiteSpace(EmailBox.Text))
            return Fail("OrderEdit.Validate.EmailRequired", EmailErrorText, EmailBox);

        if (_totalAmount < 0)
            return Fail("OrderEdit.Validate.TotalAmount", null, null);

        if (!ValidateSplitAllocations())
            return false;

        if ((StatusBox.SelectedItem as ComboBoxItem)?.Tag is not OrderStatus selectedStatus)
            return Fail("OrderEdit.Validate.Status", null, StatusBox);

        if (selectedStatus == OrderStatus.Shipped && string.IsNullOrWhiteSpace(AddressBox.Text))
            return Fail("OrderEdit.Validate.AddressRequired", AddressErrorText, AddressBox);

        if (selectedStatus is OrderStatus.Cancelled or OrderStatus.Returned && !ValidateStatusReason())
            return false;

        status = selectedStatus;
        return true;
    }

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

    /// <summary>
    /// Flags every one of <paramref name="fields"/> that is blank, all at once, and focuses the first.
    /// </summary>
    /// <remarks>
    /// One pass, not fail-fast. Fail-fast could only ever name the first missing field, and "the
    /// customer name and the mobile number are missing" is two facts — a form that discloses them one
    /// save at a time makes the user learn its rules by trial.
    /// </remarks>
    private bool TryRequireFilled(IEnumerable<RequiredTextField> fields)
    {
        var missing = fields.Where(field => field.IsBlank()).ToList();
        if (missing.Count == 0)
            return true;

        foreach (var field in missing)
            SetFieldError(field.Error, field.Message);

        RecordValidationFailure(missing.Select(field => field.Message));
        missing[0].Focus();
        return false;
    }

    // A cancelled/returned order must always carry a reason: a preset category is required
    // (defaulted so this only fails if somehow cleared), and choosing "Other" additionally
    // requires the free-text detail to be filled in.
    private bool ValidateStatusReason()
    {
        var category = (StatusReasonCategoryBox.SelectedItem as ComboBoxItem)?.Tag as string;
        if (string.IsNullOrWhiteSpace(category))
            return Fail("OrderEdit.Validate.StatusReasonRequired", StatusReasonCategoryErrorText, StatusReasonCategoryBox);

        if (category == OtherStatusReasonTag && string.IsNullOrWhiteSpace(StatusReasonBox.Text))
            return Fail("OrderEdit.Validate.StatusReasonOtherRequired", StatusReasonErrorText, StatusReasonBox);

        return true;
    }

    /// <summary>
    /// Reports one validation failure on every surface at once, and returns false so a check can be
    /// written as <c>return Fail(...)</c>.
    /// </summary>
    /// <remarks>
    /// The three surfaces answer three different questions and a failure needs all of them: the popup
    /// says something is wrong NOW (the Save button is at the foot of a form taller than the window,
    /// so a message that only appears elsewhere can be missed entirely), the banner says what, and the
    /// inline block says where. Routing every check through here is what stops them diverging — the
    /// previous code had five of eleven checks popping up a dialog and two writing anything under a
    /// field, with no rule behind which.
    ///
    /// <paramref name="inline"/> is null where there is nothing to sit under: a computed total, or a
    /// check whose own validator has already written the message itself.
    /// </remarks>
    private bool Fail(string messageKey, TextBlock? inline, Control? focus)
    {
        var message = _localization[messageKey];

        if (inline is not null)
            SetFieldError(inline, message);

        RecordValidationFailure(new[] { message });
        focus?.Focus();
        return false;
    }

    /// <summary>Adds failures to the banner and to what the dialog will say. No dialog of its own.</summary>
    /// <remarks>
    /// Newline-joined rather than run together with <c>Format.ListSeparator</c>: these are whole
    /// sentences, and "Please enter the customer name, The phone number cannot be empty" reads as a
    /// mistake in every language that capitalises.
    /// </remarks>
    private void RecordValidationFailure(IEnumerable<string> messages)
    {
        _validationProblems.AddRange(messages);

        ValidationBannerText.Text = string.Join(Environment.NewLine, _validationProblems);
        ValidationBanner.Visibility = Visibility.Visible;

        // The foot-of-window line is for a save that THREW; a stale one beside the button would read
        // as a second, unrelated problem.
        ErrorText.Text = string.Empty;
    }

    /// <summary>The one dialog, saying everything that was marked.</summary>
    private void AnnounceValidationFailure()
        => MessageBox.Show(
            string.Join(Environment.NewLine, _validationProblems),
            _localization[ValidationTitleKey],
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

    /// <summary>
    /// Wipes the banner and every inline message, so a validation pass reports the form as it is NOW.
    /// </summary>
    /// <remarks>
    /// Without this a field fixed between two attempts keeps its red line, which is worse than never
    /// having shown one: the user corrected the thing they were told about and the form still accuses
    /// them of it.
    /// </remarks>
    private void ClearValidationErrors()
    {
        _validationProblems.Clear();
        ValidationBannerText.Text = string.Empty;
        ValidationBanner.Visibility = Visibility.Collapsed;

        foreach (var block in ValidationErrorBlocks())
            SetFieldError(block, null);
    }

    private IEnumerable<TextBlock> ValidationErrorBlocks()
    {
        yield return OrderNumberErrorText;
        yield return CustomerNameErrorText;
        yield return PhoneErrorText;
        yield return EmailErrorText;
        yield return AddressErrorText;
        yield return StatusReasonCategoryErrorText;
        yield return StatusReasonErrorText;
    }

    /// <summary>Adds the new order and returns the number it was given.</summary>
    private string AddNewOrder(AppDbContext db, OrderSaveData data)
    {
        var newOrder = new Order
        {
            OrderNumber = ResolveNewOrderNumber(db),
            OrderDate = DateTime.UtcNow,
            Items = data.ClothingItems
        };
        ApplyEditableFields(newOrder, data);
        // A new order is a change by definition, so it is stamped unconditionally. Only an EDIT can
        // turn out to have altered nothing.
        StampLastModified(newOrder);
        db.Orders.Add(newOrder);

        return newOrder.OrderNumber;
    }

    /// <summary>
    /// The number this order is actually saved under. What the box shows was only a preview, and
    /// the shop may have booked other orders since this window opened, so the number is re-drawn
    /// here — unless the user typed one of their own, which always wins.
    /// </summary>
    private string ResolveNewOrderNumber(AppDbContext db)
    {
        var typed = OrderNumberBox.Text.Trim();
        var shop = ShopContext.Instance.RequireCurrent();

        var stillThePreview = string.Equals(
            typed, OrderNumberFormatter.Preview(shop, DateTime.Now), StringComparison.Ordinal);

        return stillThePreview ? OrderNumberFormatter.Reserve(db, shop, DateTime.Now) : typed;
    }

    // Moves the shop's running number past the one just used, and persists it.
    private static void AdvanceShopReceiptCounter(string orderNumber)
        => ShopContext.Instance.UpdateActiveShop(
            shop => OrderNumberFormatter.CommitSequence(shop, orderNumber, DateTime.Now));

    private async Task UpdateExistingOrderAsync(AppDbContext db, OrderSaveData data)
    {
        var order = await db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == _existing!.Id);

        if (order is null)
            return;

        ApplyEditableFields(order, data);

        // The items are replaced only when they actually differ. The old code removed and re-added
        // them every time, which made the change check below answer "changed" for every save — the
        // one thing it must not do.
        var itemsChanged = !ClothingItemsMatch(order.Items, data.ClothingItems);
        if (itemsChanged)
        {
            db.OrderItems.RemoveRange(order.Items);
            order.Items.Clear();
            foreach (var clothingItem in data.ClothingItems)
                order.Items.Add(clothingItem);
        }

        // Ask EF whether anything actually moved, rather than comparing the form to a snapshot taken
        // when the window opened. EF holds the values the row was LOADED with and compares column by
        // column, so this covers every mapped field — including the JSON blobs the form does not
        // model as fields — and it keeps covering a column added next year without anyone
        // remembering to extend a list. Reading Entry() runs change detection.
        if (itemsChanged || db.Entry(order).Properties.Any(property => property.IsModified))
            StampLastModified(order);
    }

    /// <summary>
    /// Whether the clothing lines on the form are the ones already stored, line for line.
    /// </summary>
    /// <remarks>
    /// Position matters, so this is a pairwise walk rather than a set comparison: the list is the
    /// order the shop typed and reordering it is an edit a reader would notice on the receipt.
    /// Existing rows are taken in <c>Id</c> order because that is the order they were inserted in,
    /// which is the row order that was on screen when they were saved.
    /// </remarks>
    private static bool ClothingItemsMatch(ICollection<OrderItem> stored, IReadOnlyList<OrderItem> onForm)
    {
        if (stored.Count != onForm.Count)
            return false;

        var ordered = stored.OrderBy(item => item.Id).ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            if (!string.Equals(ordered[i].ProductName, onForm[i].ProductName, StringComparison.Ordinal)
                || ordered[i].Quantity != onForm[i].Quantity
                || ordered[i].UnitPrice != onForm[i].UnitPrice
                || ordered[i].PromotionalPrice != onForm[i].PromotionalPrice)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Records who saved this order and when.</summary>
    /// <remarks>
    /// Called only where something actually changed. Opening an order, looking at it and pressing
    /// Save is not a modification, and stamping it as one overwrites a real record of who last
    /// touched the order with the name of whoever last read it.
    ///
    /// The name comes from the session rather than from anything on the form — "who saved this" is
    /// not a field anybody should be able to type. Left untouched when nobody is signed in (only
    /// reachable from a harness), so a save can never blank a name a real crew member left behind.
    /// </remarks>
    private static void StampLastModified(Order order)
    {
        order.LastModifiedDate = DateTime.UtcNow;

        if (AuthenticationService.Instance.CurrentUser is { } crew)
            order.LastModifiedBy = crew.DisplayLabel;
    }

    private void ApplyEditableFields(Order order, OrderSaveData data)
    {
        order.CustomerName = CustomerNameBox.Text.Trim();
        // Stored with its dial code in front, in the same column it always used: "+1 905-401-6667".
        order.PhoneNumber = PhoneField.FullNumber;
        ApplyPaymentSplits(order);
        order.Email = string.IsNullOrWhiteSpace(EmailBox.Text) ? null : EmailBox.Text.Trim();
        order.Address = string.IsNullOrWhiteSpace(AddressBox.Text) ? null : AddressBox.Text.Trim();
        ApplyStatusReasonFields(order, data.Status);
        order.ServiceType = data.ServiceType;
        order.ServiceDetails = (AlterationCategoryBox.SelectedItem as ComboBoxItem)?.Tag as string;
        order.AdditionalNotes = NullIfWhiteSpace(AlterationAdditionalNotesBox.Text);
        order.Subtotal = data.Subtotal;
        order.TaxRate = data.TaxRate;
        order.ChestSize = null;
        order.JacketLength = null;
        order.CustomMadeRecordsJson = data.CustomMadeJson;
        order.Status = data.Status;
        order.TotalAmount = _totalAmount;
        // The order records the money it was priced in. This line is the reason the column exists.
        // Until it was added nothing wrote the column, so every saved order carried the enum default
        // regardless of what its shop actually traded in.
        order.CurrencyType = SelectedCurrency;
        order.Notes = NullIfWhiteSpace(NotesBox.Text);
        // The audit stamp is deliberately NOT written here. This method assigns whatever the form
        // holds, which is how the change check works — every assignment of an unchanged value leaves
        // EF's IsModified false. Writing a fresh timestamp in here would make every save look like a
        // change, including the ones that are not. See StampLastModified.
        ApplyPaymentFields(order);
    }

    // Persists the preset category (only meaningful for cancelled/returned orders) and the
    // free-text detail (only meaningful when that category is "Other"); both are cleared
    // once the order is no longer cancelled/returned.
    private void ApplyStatusReasonFields(Order order, OrderStatus status)
    {
        if (status is not (OrderStatus.Cancelled or OrderStatus.Returned))
        {
            order.StatusReasonCategory = null;
            order.StatusReason = null;
            return;
        }

        var category = (StatusReasonCategoryBox.SelectedItem as ComboBoxItem)?.Tag as string;
        order.StatusReasonCategory = category;
        order.StatusReason = category == OtherStatusReasonTag ? NullIfWhiteSpace(StatusReasonBox.Text) : null;
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

    // Deposit amount edits may fully cover a section, so re-run the auto-complete pass.
    private void OnDownpaymentAmountChanged(object sender, TextChangedEventArgs e)
    {
        // Changing the deposit invalidates the manual "deposit received" confirmation
        // and forces the final balance to be recalculated.
        if (!_syncingPayment && sender is TextBox box
            && GetDownCompletedCheckForBox(box) is { } completedCheck)
        {
            _syncingPayment = true;
            try
            {
                completedCheck.IsChecked = false;
            }
            finally
            {
                _syncingPayment = false;
            }
        }

        if (!_syncingPayment && sender is TextBox depositBox)
            EnforceDepositCeiling(depositBox);

        RefreshComputedTotals();
    }

    /// <summary>
    /// A deposit can never exceed its section's pre-tax service total. CalculateSectionPayment
    /// already clamps it, but silently — which hides a typo behind numbers that quietly stop
    /// responding. This tells the shop what happened and pins the deposit to the total, so the
    /// entered value and the calculated one always agree.
    /// </summary>
    private void EnforceDepositCeiling(TextBox depositBox)
    {
        // A re-entrancy guard of its own: writing the corrected value raises TextChanged again,
        // and a modal dialog pumps messages, so without this the warning can stack up.
        if (_enforcingDepositCeiling)
            return;

        var section = Array.Find(AllSections, c => c.DownpaymentBox == depositBox);
        if (section is null)
            return;

        // Nothing to cap against until the service is priced.
        var subtotal = section.SectionSubtotal();
        if (subtotal <= 0m || ParseDecimalOrZero(depositBox.Text) <= subtotal)
            return;

        _enforcingDepositCeiling = true;
        try
        {
            MessageBox.Show(
                _localization.Format("OrderEdit.Warn.DepositExceedsTotal",
                    _localization[section.ServiceNameKey], FormatCurrency(subtotal)),
                _localization[ValidationTitleKey],
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            _syncingPayment = true;
            try
            {
                depositBox.Text = subtotal.ToString("0.##");
            }
            finally
            {
                _syncingPayment = false;
            }

            depositBox.CaretIndex = depositBox.Text.Length;
        }
        finally
        {
            _enforcingDepositCeiling = false;
        }
    }

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

    private void OnAddCustomMadeRecordClick(object sender, RoutedEventArgs e)
    {
        if (_isReadOnly)
            return;

        if (!CanOpenCustomMadeWindow())
            return;

        var dialog = new CustomMadeServiceWindow(
            _localization,
            defaultOrderNumber: OrderNumberBox.Text,
            defaultCustomerName: CustomerNameBox.Text,
            defaultPhoneNumber: PhoneField.FullNumber,
            defaultEmail: EmailBox.Text,
            isReadOnly: false)
        {
            Owner = this
        };

        if (dialog.ShowDialog() is true && dialog.Result is not null)
        {
            _customMadeRecords.Add(dialog.Result);
            RefreshCustomMadeEmptyState();
            RefreshComputedTotals();
        }
    }

    private void OnEditCustomMadeRecordClick(object sender, RoutedEventArgs e)
    {
        if (CustomMadeRecordsList.SelectedItem is not CustomMadeServiceRecord selected)
            return;

        // A settled custom-made section (final balance cleared) is locked: the record
        // opens in view mode (title from the OrderEdit.ViewCustomMade key) with every
        // field — including the document upload area — read-only, mirroring the
        // whole-order read-only path.
        var recordReadOnly = _isReadOnly || IsSettled(_customMadeControls);

        if (!recordReadOnly && !CanOpenCustomMadeWindow())
            return;

        var dialog = new CustomMadeServiceWindow(
            _localization,
            existing: selected,
            defaultOrderNumber: OrderNumberBox.Text,
            defaultCustomerName: CustomerNameBox.Text,
            defaultPhoneNumber: PhoneField.FullNumber,
            defaultEmail: EmailBox.Text,
            isReadOnly: recordReadOnly)
        {
            Owner = this
        };

        if (recordReadOnly)
        {
            dialog.ShowDialog();
            return;
        }

        if (dialog.ShowDialog() is true && dialog.Result is not null)
        {
            var index = _customMadeRecords.IndexOf(selected);
            if (index >= 0)
                _customMadeRecords[index] = dialog.Result;
            RefreshCustomMadeEmptyState();
            RefreshComputedTotals();
        }
    }

    private void OnRemoveCustomMadeRecordClick(object sender, RoutedEventArgs e)
    {
        if (_isReadOnly)
            return;

        if (CustomMadeRecordsList.SelectedItem is not CustomMadeServiceRecord selected)
            return;

        _customMadeRecords.Remove(selected);
        RefreshCustomMadeEmptyState();
        RefreshComputedTotals();
    }

    private void OnCustomMadeRecordsDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CustomMadeRecordsList.SelectedItem is not CustomMadeServiceRecord)
            return;

        OnEditCustomMadeRecordClick(sender, new RoutedEventArgs());
    }

    // Requirement 4a: pressing Enter on a selected record opens the same editor
    // dialog as a double-click.
    private void OnCustomMadeRecordsKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        if (CustomMadeRecordsList.SelectedItem is not CustomMadeServiceRecord)
            return;

        e.Handled = true;
        OnEditCustomMadeRecordClick(sender, new RoutedEventArgs());
    }

    private void OnEmailBoxLostFocus(object sender, RoutedEventArgs e) => ValidateEmailField();

    private void OnPhoneFieldCommitted(object? sender, EventArgs e) => ValidatePhoneField();

    // Requirement 5b - an entered email must be well formed. Empty stays allowed
    // here because the payment flow separately enforces email for e-transfer.
    private bool ValidateEmailField()
    {
        var valid = ContactValidation.IsValidEmail(EmailBox.Text);
        SetFieldError(EmailErrorText, valid ? null : _localization["OrderEdit.Validate.EmailInvalid"]);
        return valid;
    }

    /// <summary>
    /// The number must be a possible one in the country picked for it — unless it is a stored number
    /// nobody has touched.
    /// </summary>
    /// <remarks>
    /// The lenient rule (shape, and 7 to 15 digits) exists for numbers that predate the per-country
    /// length rule: holding those to it would mean an order taken last year could not be saved again
    /// — its status could not be corrected, its balance could not be cleared — until somebody re-typed
    /// a phone number they have no way to verify.
    ///
    /// That argument covers the STORED VALUE and nothing else, which is why the choice is made on
    /// <see cref="PhoneNumberField.HasBeenEdited"/> and not on whether the order is new. Keying it to
    /// the order meant an existing one accepted ANY 7-to-15-digit number in any country: a probe
    /// across every shipped country from 6 to 13 digits found the two rules disagreeing on every
    /// length but the correct one, and the lenient answer winning every time. A number typed just now
    /// is typed with the customer standing there, whatever order it belongs to.
    /// </remarks>
    private bool ValidatePhoneField()
    {
        // The rule lives on the control, so this window and the custom-made record editor cannot
        // drift apart — the field is hosted by both and used to be checked by only one.
        var valid = PhoneField.IsAcceptable;
        var message = PhoneField.HasBeenEdited || _existing is null
            ? PhoneField.ValidationMessage
            : _localization["OrderEdit.Validate.PhoneInvalid"];

        SetFieldError(PhoneErrorText, valid ? null : message);
        PhoneField.MarkInvalid(!valid);
        return valid;
    }

    /// <summary>
    /// Clears a field's own message as soon as it is typed into, so the correction is acknowledged
    /// where it was made rather than at the next Save.
    /// </summary>
    /// <remarks>
    /// Wired in code from one map rather than as five <c>TextChanged</c> attributes in the XAML: the
    /// pairing of a box with its message block already exists here, and a second copy of it in markup
    /// is the thing that goes stale when a field is added. Only clears — it does not re-validate, so
    /// nothing turns red while somebody is halfway through typing an address.
    /// </remarks>
    private void RegisterValidationClearing()
    {
        var pairs = new (TextBox Box, TextBlock Error)[]
        {
            (OrderNumberBox, OrderNumberErrorText),
            (CustomerNameBox, CustomerNameErrorText),
            (EmailBox, EmailErrorText),
            (AddressBox, AddressErrorText),
            (StatusReasonBox, StatusReasonErrorText),
        };

        foreach (var (box, error) in pairs)
            box.TextChanged += (_, _) => SetFieldError(error, null);

        // The phone is not a TextBox any more, and its message must clear on a change to EITHER half —
        // switching the country is as much a correction as retyping the digits.
        PhoneField.PhoneChanged += (_, _) =>
        {
            SetFieldError(PhoneErrorText, null);
            PhoneField.MarkInvalid(false);
        };
        PhoneField.PhoneCommitted += OnPhoneFieldCommitted;
    }

    private static void SetFieldError(TextBlock target, string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            target.Text = string.Empty;
            target.Visibility = Visibility.Collapsed;
        }
        else
        {
            target.Text = message;
            target.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// The custom-made editor needs a customer to attach its record to. Routed through the same guard
    /// as Save, so being stopped here marks the same fields in the same places rather than raising a
    /// dialog and leaving the form looking untouched.
    /// </summary>
    private bool CanOpenCustomMadeWindow()
    {
        ClearValidationErrors();
        if (TryRequireFilled(CustomerContactFields()))
            return true;

        AnnounceValidationFailure();
        return false;
    }

    private void RegisterDecimalTextBoxes()
    {
        // Every money input gets the same treatment: digits-only filtering, paste filtering,
        // and the zero-clearing focus behaviour that stops "0" turning into "012".
        // The alteration price opts out of restore-zero-on-blur: a BLANK price box is what marks
        // the alteration service as absent from the order (HasItems), so turning it into "0"
        // would silently enrol the service as an unpriced one.
        RegisterMoneyBox(AlterationPriceBox, restoreZeroOnBlur: false);
        RegisterMoneyBox(AlterationDownpaymentBox);
        RegisterMoneyBox(ClothingDownpaymentBox);
        RegisterMoneyBox(CustomMadeDownpaymentBox);
    }

    /// <summary>
    /// Wires the shared money-input behaviour. Clothing item rows are created at runtime and
    /// call this too, so every price box in the window behaves identically.
    /// </summary>
    /// <param name="restoreZeroOnBlur">
    /// Pass false for a box where BLANK carries its own meaning and must not become "0" —
    /// an optional promotional price, or the alteration price box whose emptiness marks the
    /// service as absent. The zero-clearing focus behaviour still applies either way.
    /// </param>
    private void RegisterMoneyBox(TextBox box, bool restoreZeroOnBlur = true)
    {
        RegisterDecimalTextBox(box);
        box.GotFocus += OnMoneyBoxGotFocus;

        if (restoreZeroOnBlur)
            box.LostFocus += OnMoneyBoxLostFocus;
    }

    // A box already showing 0 is cleared on entry, so typing "12" gives "12" rather than
    // "012" — the caret would otherwise land after the existing zero. Leaving the box empty
    // or invalid restores a valid zero on exit.
    private void OnMoneyBoxGotFocus(object sender, RoutedEventArgs e)
    {
        // IsReadOnly is checked as well as IsEnabled: a read-only box (e.g. a tax box while the
        // stage is settled by cash) still takes focus, and clearing its text programmatically
        // would succeed and blank a value the user is not allowed to change.
        if (sender is not TextBox box || !box.IsEnabled || box.IsReadOnly)
            return;

        if (box.Text.Length > 0 && ParseDecimalOrZero(box.Text) == 0m)
        {
            _syncingPayment = true;
            box.Clear();
            _syncingPayment = false;
        }
        box.SelectAll();
    }

    private void OnMoneyBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box)
            return;

        if (string.IsNullOrWhiteSpace(box.Text) || !decimal.TryParse(box.Text, out _))
        {
            _syncingPayment = true;
            box.Text = "0";
            _syncingPayment = false;
            // runAutoComplete stays false: restoring a zero must not move a payment method.
            // The deposit boxes' own TextChanged handler already ran the auto-complete pass.
            RefreshComputedTotals(runAutoComplete: false);
        }
    }

    // Static now that the paste handler it attaches is: nothing here touches the window.
    private static void RegisterDecimalTextBox(TextBox textBox)
    {
        DataObject.AddPastingHandler(textBox, OnDecimalTextBoxPaste);
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

    /// <summary>
    /// Static: attached only through <c>DataObject.AddPastingHandler</c> from code, never named in
    /// XAML. A handler XAML wires up cannot be static, because the generated InitializeComponent
    /// references it as <c>this.Handler</c>.
    /// </summary>
    private static void OnDecimalTextBoxPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        if (!e.SourceDataObject.GetDataPresent(DataFormats.Text))
        {
            e.CancelCommand();
            return;
        }

        var pastedText = e.SourceDataObject.GetData(DataFormats.Text) as string ?? string.Empty;
        var proposedText = GetProposedText(textBox, pastedText);
        if (!DecimalInputPattern.IsMatch(proposedText))
            e.CancelCommand();
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

    private void RefreshComputedTotals(bool runAutoComplete = true)
    {
        RefreshAlterationTotals();
        RefreshClothingTotals();
        RefreshCustomMadeTotals();
        RefreshAllServicesTotalAmount();

        // Bug 3: editing the price/tax only recomputes amounts; it must not touch
        // the payment method selections. Auto-complete runs only on deposit/method changes.
        if (runAutoComplete)
            AutoCompleteFullyPaidSections();

        // A cleared section is settled, so its pricing inputs (price/tax and the
        // item/record editors that feed the total) must be locked too. Runs last so
        // it wins over the tax-box enabling done inside the Refresh*Totals passes.
        RefreshPricingLocks();
    }

    // A section counts as settled — and therefore locked against further edits — only when
    // it is marked cleared AND actually carries a charge. The charge test matters: a section
    // with no charge reports "cleared" simply because nothing is owed on it, and locking on
    // that alone traps the user. The price inputs would be frozen at zero, so the section
    // could never be given a price to un-clear it, and the deposit radios would stop
    // responding entirely — the section looks dead on reopen.
    private static bool IsSettled(PaymentSectionControls c)
        => c.BalanceClearedCheck.IsChecked is true && c.SectionTotal() > 0m;

    // Centralized lock manager for a single payment section's input controls.
    // All IsReadOnly decisions live here; no Refresh*Totals method touches lock state.
    private void ApplySectionInputLocks(PaymentSectionControls c, TextBox? priceBox)
    {
        var downCompleted = c.DownCompletedCheck.IsChecked is true;
        var sectionLocked = _isReadOnly || _isRefunded || IsSettled(c) || c.IsServiceSwitchedOff;
        var inputsLocked = sectionLocked || downCompleted;

        if (priceBox is not null)
            priceBox.IsReadOnly = inputsLocked;

        // The tax rate has no lock state of its own any more: it is a store-wide rule shown as a
        // fixed value, so there is nothing here that could be typed into.
        c.DownpaymentBox.IsReadOnly = inputsLocked;
    }

    private void RefreshPricingLocks()
    {
        // Re-apply the enable-state locks as well as the read-only ones. These two used to be
        // driven by different triggers: IsReadOnly here on every refresh, IsEnabled only via
        // UpdatePaymentVisibility, which RefreshComputedTotals skips when runAutoComplete is
        // false. Both now depend on values that a plain refresh can change — the alteration
        // category (IsServiceSwitchedOff) and the section total (IsSettled) — so leaving them on
        // separate triggers stranded the deposit radios and checkboxes in a stale state while the
        // price box unlocked correctly. ApplySectionLock only assigns IsEnabled, with no text
        // writes, so calling it here cannot re-enter this method.
        ApplySectionLock(_alterationControls);
        ApplySectionLock(_customMadeControls);
        ApplySectionLock(_clothingControls);

        ApplySectionInputLocks(_alterationControls, AlterationPriceBox);
        // Additional notes belong to the alteration service, so they lock with it.
        AlterationAdditionalNotesBox.IsReadOnly = _isReadOnly || _isRefunded || AlterationServiceSwitchedOff;
        ApplySectionInputLocks(_customMadeControls, priceBox: null);
        ApplySectionInputLocks(_clothingControls, priceBox: null);

        // Section-level controls not captured inside PaymentSectionControls. Same IsSettled
        // rule as above, so a section with no charge keeps its item editors usable — that is
        // the only way to give it a price.
        var customMadeSectionLocked = _isReadOnly || _isRefunded || IsSettled(_customMadeControls);
        AddCustomMadeButton.IsEnabled = !customMadeSectionLocked;
        RemoveCustomMadeButton.IsEnabled = !customMadeSectionLocked;
        RefreshCustomMadeButtonLabel();

        var clothingSectionLocked = _isReadOnly || _isRefunded || IsSettled(_clothingControls);
        AddItemButton.IsEnabled = !clothingSectionLocked;
        SetClothingRowsLocked(clothingSectionLocked);
    }

    private void SetClothingRowsLocked(bool locked)
    {
        foreach (var row in _clothingItemRows)
        {
            row.CategoryBox.IsEnabled = !locked;
            row.UnitPriceBox.IsReadOnly = locked;
            row.PromotionalPriceBox.IsReadOnly = locked;
            row.RemoveButton.IsEnabled = !locked;
        }
    }

    /// <summary>
    /// When a section's deposit fully covers its total (final balance reaches zero),
    /// mirror the deposit method onto the final balance and mark the section cleared.
    /// </summary>
    private void AutoCompleteFullyPaidSections()
    {
        if (_syncingPayment)
            return;

        _syncingPayment = true;
        try
        {
            _alterationAutoCompleted = AutoCompleteSection(_alterationAutoCompleted, _alterationSubtotal, _alterationControls);
            _customMadeAutoCompleted = AutoCompleteSection(_customMadeAutoCompleted, _customMadeSubtotal, _customMadeControls);
            _clothingAutoCompleted = AutoCompleteSection(_clothingAutoCompleted, _clothingSubtotal, _clothingControls);
        }
        finally
        {
            _syncingPayment = false;
        }

        UpdatePaymentVisibility();
        RefreshPaymentSummary();
    }

    private static bool AutoCompleteSection(bool wasAutoCompleted, decimal subtotalBase, PaymentSectionControls c)
    {
        var downMethod = GetSelectedDownMethod(c);
        var downpayment = ParseDecimalOrZero(c.DownpaymentBox.Text);
        var hasRealDownMethod = downMethod is not null && downMethod != PaymentMethod.None;
        // Bug 1: the deposit-received checkbox is manual; auto-fill only reacts to it.
        var depositReceived = c.DownCompletedCheck.IsChecked is true;
        // The deposit is a pre-tax amount, so it fully covers the section when it reaches
        // the pre-tax subtotal (any card tax is added on top and not owed as a balance).
        var fullyPaid = subtotalBase > 0m && downpayment >= subtotalBase && hasRealDownMethod;

        if (fullyPaid && depositReceived)
        {
            // Only on ENTRY into the fully-paid state, never on every refresh. Re-evaluating the
            // condition each pass made the tick impossible to remove: unticking it (or the master
            // "clear all balances") put it straight back on the next time anything recomputed, so a
            // fully-deposited section could never be re-opened. The auto-complete is a convenience
            // for the moment the deposit covers the total — not a rule the user has to keep losing
            // an argument with. `wasAutoCompleted` stays true, so the state is remembered and
            // re-arms only when the deposit or the received tick actually changes.
            if (!wasAutoCompleted)
            {
                SetSelectedFinalMethod(c, downMethod);
                c.BalanceClearedCheck.IsChecked = true;
            }

            return true;
        }

        // Deposit no longer covers the total (or deposit-received was unchecked):
        // reinitialize only what we auto-filled. The deposit-received checkbox stays manual.
        if (wasAutoCompleted)
        {
            SetSelectedFinalMethod(c, null);
            c.BalanceClearedCheck.IsChecked = false;
        }

        // Bug 1: once the deposit is marked received, default the final method to mirror
        // the deposit method until the user changes it.
        if (hasRealDownMethod && depositReceived && GetSelectedFinalMethod(c) is null)
            SetSelectedFinalMethod(c, downMethod);

        return false;
    }

    // The final balance inherits the deposit's payment method until the user explicitly
    // picks one of its own. Without this the section advertises a tax rate it never
    // applies: choosing Card for the deposit shows a rate on the outstanding balance while
    // the untouched final method stays null, leaving that balance untaxed. Mirrors the same
    // "default the final method from the deposit" convention already used by
    // AutoCompleteSection and ApplyClearAllToSection, and an explicit selection always wins.
    private static PaymentMethod? EffectiveFinalMethod(PaymentSectionControls c)
    {
        var chosen = GetSelectedFinalMethod(c);
        if (chosen is not null)
            return chosen;

        var downMethod = GetSelectedDownMethod(c);
        return downMethod == PaymentMethod.None ? null : downMethod;
    }

    // Seeds both stage rates for every section from a saved order. A null final rate means
    // the order predates the per-stage split, so its single stored rate keeps applying to
    // both portions. Must run AFTER LoadPaymentFields: the card/cash rule reads the payment
    // radios, and with none selected yet it would zero a stored rate before it is ever used.
    private void LoadStageTaxRates(Order existing)
    {
        LoadSectionTaxRates(_alterationControls, existing.AlterationTaxRate ?? existing.TaxRate, existing.AlterationFinalTaxRate);
        LoadSectionTaxRates(_clothingControls, existing.ClothingTaxRate ?? existing.TaxRate, existing.ClothingFinalTaxRate);
        LoadSectionTaxRates(_customMadeControls, existing.CustomMadeTaxRate, existing.CustomMadeFinalTaxRate);
    }

    private void LoadSectionTaxRates(PaymentSectionControls c, decimal? depositRate, decimal? finalRate)
    {
        c.DepositTaxRate = depositRate ?? DefaultTaxRate;
        c.FinalTaxRate = finalRate ?? c.DepositTaxRate;
        // Point the display at whichever stage the loaded order is already in.
        c.ShowingFinalRate = c.IsFinalStage;
        ShowStageRate(c);
    }

    /// <summary>
    /// Resolves both stage rates for a section and shows the one that applies now.
    ///
    /// The rate is a STORE rule, not a per-order figure: it comes from
    /// <see cref="PaymentTaxRules.Active"/> keyed on the method settling that portion, which is
    /// what makes a change in Shop Settings take effect across the shop. The one exception is a
    /// read-only order — completed, shipped, cancelled or returned. That one keeps the rates it
    /// was actually charged, because its receipt has already been printed and the screen must not
    /// disagree with the paper.
    ///
    /// A tax-INCLUSIVE order takes one rate for both portions from the jurisdiction instead — see
    /// <see cref="IncludedTaxRatePercent"/> — and never zeroes it per method, because the tax is
    /// already inside the price whatever settles it.
    /// </summary>
    private void ApplyStageTaxRates(PaymentSectionControls c)
    {
        if (PricesIncludeTax)
        {
            if (!_isReadOnly)
            {
                var includedRate = IncludedTaxRatePercent;
                c.DepositTaxRate = includedRate;
                c.FinalTaxRate = includedRate;
            }

            c.ShowingFinalRate = c.IsFinalStage;
            ShowStageRate(c);
            return;
        }

        var rules = PaymentTaxRules.Active;
        var depositMethod = GetSelectedDownMethod(c);
        var finalMethod = EffectiveFinalMethod(c);

        if (_isReadOnly)
        {
            // Still zeroed for a method the shop has since made tax free, so the figures on screen
            // always match what Order.CalculateSectionPayment will compute for the same order.
            if (!rules.IsTaxable(depositMethod))
                c.DepositTaxRate = 0m;
            if (!rules.IsTaxable(finalMethod))
                c.FinalTaxRate = 0m;
        }
        else
        {
            c.DepositTaxRate = rules.RateFor(depositMethod);
            c.FinalTaxRate = rules.RateFor(finalMethod);
        }

        c.ShowingFinalRate = c.IsFinalStage;
        ShowStageRate(c);
    }

    // Writes the current stage's rate into its (read-only) value block and names the stage.
    private void ShowStageRate(PaymentSectionControls c)
    {
        var stageRate = c.ShowingFinalRate ? c.FinalTaxRate : c.DepositTaxRate;
        c.TaxValueText.Text = FormatTaxRate(stageRate);
        UpdateTaxLabel(c);
    }

    private static string FormatTaxRate(decimal ratePercent) => TaxRateFormat.Percent(ratePercent);

    // Small print under Order.Fields.ServiceTotalTax: how the section's tax splits across the two portions
    // and which method settled each, so a $0 line reads as "that portion wasn't card"
    // rather than as a missing charge.
    private void UpdateTaxBreakdownLines(PaymentSectionControls c, SectionPayment money)
    {
        // Read off the split rather than re-derived as `Received − Deposit`: that difference is zero
        // whenever the tax is already inside the price, which printed "tax 0" beside a total that
        // was not zero. SectionPayment carries the per-portion figure for both modes.
        var depositMethod = GetSelectedDownMethod(c);
        c.DepositTaxLine.Text = _localization.Format("Order.Fields.DepositTaxLine",
            PaymentMethodName(depositMethod),
            FormatCurrency(money.DepositTax));
        c.FinalTaxLine.Text = _localization.Format("Order.Fields.FinalTaxLine",
            PaymentMethodName(EffectiveFinalMethod(c)),
            FormatCurrency(money.FinalTax));

        UpdateDueAndReceivedLines(c, money);
        // Called from here rather than from each section's refresh: both panels are then written in
        // one pass, from one reading of the split, for every section — which is the only way the two
        // views of the same order stay in step.
        UpdateInclusiveBreakdown(c, money);

        // Each stage's split allocation, against what that stage actually owes.
        RefreshSplitStage(c, finalStage: false, money.Deposit);
        RefreshSplitStage(c, finalStage: true, money.FinalBase);
    }

    /// <summary>
    /// One section's calculation input, carrying its split lines when that section's card is set to
    /// split — read live off the amount boxes, so the figures move as they are typed.
    /// </summary>
    /// <remarks>
    /// The lines are built from the CURRENT rate for each method rather than from anything stored,
    /// because this is the editor: what the shop is about to charge is what its rules say today. They
    /// are frozen onto the order at save (<c>PaymentSplitLine.RatePercent</c>), which is what keeps a
    /// reprinted receipt honest afterwards.
    /// </remarks>
    private SectionPaymentInput SectionInput(PaymentSectionControls c, decimal subtotal, decimal deposit)
        => new(subtotal, deposit, c.DepositTaxRate, c.FinalTaxRate,
            GetSelectedDownMethod(c), EffectiveFinalMethod(c), PricesIncludeTax)
        {
            DepositSplit = c.IsDepositSplit ? ReadSplitLines(c, finalStage: false) : null,
            FinalSplit = c.IsFinalSplit ? ReadSplitLines(c, finalStage: true) : null,
        };

    /// <summary>
    /// What each portion costs, beside what has actually been taken for it.
    /// </summary>
    /// <remarks>
    /// The DUE figures are the taxed amounts — `ReceivedDownpayment` and `FinalCharge` on the
    /// section split — because that is what the customer is actually asked for; the pre-tax rows
    /// above already say what the work cost. Both are shown from the start.
    ///
    /// A RECEIVED line appears only once its portion is confirmed, and it carries the same figure.
    /// Showing it from the start would state that money had been taken when it had not, and showing
    /// a zero would be worse — indistinguishable from a portion that was genuinely free. Label and
    /// value are hidden together: a lone label reads as a value that failed to load.
    ///
    /// The final balance's received line follows the section's own cleared TICK, not "is anything
    /// owed". A deposit covering the whole total leaves nothing owed, but nothing has been collected
    /// for the final portion either — and it is precisely that case where the two answers diverge.
    /// </remarks>
    private void UpdateDueAndReceivedLines(PaymentSectionControls c, SectionPayment money)
    {
        var depositDue = money.ReceivedDownpayment;
        var balanceDue = money.FinalCharge;

        c.DueDownpaymentText.Text = FormatCurrency(depositDue);
        c.FinalDueDownpaymentText.Text = FormatCurrency(depositDue);
        c.FinalDueBalanceText.Text = FormatCurrency(balanceDue);

        var depositReceived = c.DownCompletedCheck.IsChecked is true;
        c.FinalReceivedDownpaymentText.Text = FormatCurrency(depositReceived ? depositDue : 0m);
        SetLineVisible(c.FinalReceivedDownpaymentLabel, c.FinalReceivedDownpaymentText, depositReceived);

        var balanceReceived = c.BalanceClearedCheck.IsChecked is true;
        c.FinalReceivedBalanceText.Text = FormatCurrency(balanceReceived ? balanceDue : 0m);
        SetLineVisible(c.FinalReceivedBalanceLabel, c.FinalReceivedBalanceText, balanceReceived);

        // The inclusive panel is filled from the SAME figures, in the same pass. Filling it from its
        // own reading of the split is how the two panels would come to disagree about one order.
        c.IncReceivedDepositText.Text = FormatCurrency(depositReceived ? depositDue : 0m);
        SetLineVisible(c.IncReceivedDepositLabel, c.IncReceivedDepositText, depositReceived);
        c.IncDueBalanceText.Text = FormatCurrency(balanceDue);
        c.IncReceivedBalanceText.Text = FormatCurrency(balanceReceived ? balanceDue : 0m);
        SetLineVisible(c.IncReceivedBalanceLabel, c.IncReceivedBalanceText, balanceReceived);
    }

    /// <summary>
    /// The rows the inclusive panel owns alone: the tax-inclusive price, what is still outstanding,
    /// and the line naming the tax already inside that price. Everything else it shows is written by
    /// <see cref="UpdateDueAndReceivedLines"/>, which fills both panels from one reading of the split.
    /// </summary>
    /// <remarks>
    /// Runs whatever the pricing mode: the panel it writes into is collapsed in the other one, and a
    /// guard here would only mean the rows were stale the moment a shop's location changed under an
    /// order being edited. The tax line is skipped when nothing is taxed — "Includes VAT (0%): 0.00"
    /// is noise, and a zero-rated inclusive order is exactly the case where it would appear.
    /// </remarks>
    private void UpdateInclusiveBreakdown(PaymentSectionControls c, SectionPayment money)
    {
        // Same rule as every other residual on this screen: a cleared section owes nothing.
        var residual = c.BalanceClearedCheck.IsChecked is true ? 0m : money.FinalCharge;

        c.IncTotalText.Text = FormatCurrency(money.Subtotal);
        c.IncResidualText.Text = FormatCurrency(residual);

        // Either stage rate would do — they are the same number in this mode — but the tax must
        // actually be non-zero as well, or a section priced at zero would advertise a rate it never
        // charged anything at.
        var rate = c.DepositTaxRate;
        var taxed = money.Tax > 0m && rate > 0m;
        if (taxed)
        {
            c.IncTaxLabel.Text = _localization.Format("Order.Fields.IncludedTaxLabel",
                ShopTaxName, TaxRateFormat.Text(rate));
            c.IncTaxText.Text = FormatCurrency(money.Tax);
        }

        SetLineVisible(c.IncTaxLabel, c.IncTaxText, taxed);
    }

    private static void SetLineVisible(TextBlock label, TextBlock value, bool visible)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        label.Visibility = visibility;
        value.Visibility = visibility;
    }

    // Normalized so an order still carrying the legacy single "Card" value reads as Debit Card
    // rather than the retired label.
    private string PaymentMethodName(PaymentMethod? method)
        => _localization[$"PaymentMethod.{PaymentTaxRules.Normalize(method ?? PaymentMethod.None)}"];

    /// <summary>
    /// Names the stage the tax box is showing, so a rate here is never mistaken for the other
    /// portion's — except where the price already contains the tax, which has no stages to tell
    /// apart: a value-added tax is a property of the sale, so the deposit and the final balance
    /// carry the same rate by construction. There the label names the TAX instead ("VAT Rate"),
    /// which is also the only place that rate appears once the deposit-stage breakdown is gone.
    /// </summary>
    private void UpdateTaxLabel(PaymentSectionControls c)
    {
        if (PricesIncludeTax)
        {
            c.TaxLabel.Text = _localization.Format("Order.Fields.IncludedTaxRateLabel", ShopTaxName);
            return;
        }

        c.TaxLabel.Text = _localization[c.ShowingFinalRate
            ? "Order.Fields.FinalTaxRate"
            : "Order.Fields.DepositTaxRate"];
    }

    /// <summary>What this shop's location calls its tax, from its <c>TaxName.*</c> key.</summary>
    private string ShopTaxName => TaxJurisdictions.TaxName(ShopContext.Instance.Current, _localization);

    private void RefreshAlterationTotals()
    {
        // A switched-off alteration service contributes nothing, whatever the price box holds —
        // the value is kept so switching the category back restores it.
        var price = AlterationServiceSwitchedOff ? 0m : ParseDecimalOrZero(AlterationPriceBox.Text);
        // Resolves both stage rates and points the shared tax box at the current stage.
        // Cash/Etransfer portions are forced to 0%, so the displayed rate always matches
        // what is actually charged.
        ApplyStageTaxRates(_alterationControls);
        var downpayment = ParseDecimalOrZero(AlterationDownpaymentBox.Text);
        var money = Order.CalculateSectionPayment(SectionInput(_alterationControls, price, downpayment));
        // A cleared balance means nothing is still owed for this section.
        var residual = AlterationBalanceClearedCheck.IsChecked.GetValueOrDefault() ? 0m : money.FinalCharge;

        _alterationSubtotal = price;
        _alterationSumTotal = money.Total;
        _alterationMoney = money;

        // Deposit-stage rows are scoped to that stage and add up: subtotal + deposit tax — or just
        // the subtotal when the tax is already inside it. SectionPayment owns that rule.
        //
        // The tax row shows the DEPOSIT portion's tax alone; the final portion's joins only at the
        // final stage, whose panel shows the complete figure. Stage-scoping it is what makes the
        // deposit amount visibly move it: a section's TOTAL tax is invariant to the deposit split
        // whenever both portions share a rate (deposit*r + (subtotal−deposit)*r == subtotal*r), so
        // showing the total here made the row look frozen.
        AlterationSubtotalText.Text = FormatCurrency(price);
        // Pre-tax balance still to come: the subtotal less the deposit, before any card tax.
        AlterationPreTaxBalanceText.Text = FormatCurrency(money.FinalBase);
        AlterationDepositTaxText.Text = FormatCurrency(money.DepositTax);
        AlterationSumTotalText.Text = FormatCurrency(money.DepositStageTotal);
        AlterationFinalPriceDisplayText.Text = FormatCurrency(price);
        AlterationFinalDownpaymentDisplayText.Text = FormatCurrency(money.Deposit);
        AlterationFinalPreTaxBalanceText.Text = FormatCurrency(money.FinalBase);
        AlterationFinalTotalTaxText.Text = FormatCurrency(money.Tax);
        UpdateTaxBreakdownLines(_alterationControls, money);
        AlterationFinalTotalText.Text = FormatCurrency(money.Total);
        AlterationResidualText.Text = FormatCurrency(residual);
    }

    private void RefreshClothingTotals()
    {
        decimal subtotal = 0m;
        foreach (var row in _clothingItemRows)
        {
            var rowSubtotal = GetClothingItemSubtotal(row);
            row.SubtotalText.Text = FormatCurrency(rowSubtotal);
            subtotal += rowSubtotal;
        }

        // See RefreshAlterationTotals: resolves both stage rates and retargets the tax box.
        ApplyStageTaxRates(_clothingControls);
        var downpayment = ParseDecimalOrZero(ClothingDownpaymentBox.Text);
        var money = Order.CalculateSectionPayment(SectionInput(_clothingControls, subtotal, downpayment));
        // A cleared balance means nothing is still owed for this section.
        var residual = ClothingBalanceClearedCheck.IsChecked.GetValueOrDefault() ? 0m : money.FinalCharge;

        _clothingSubtotal = subtotal;
        _clothingSumTotal = money.Total;
        _clothingMoney = money;

        // Deposit-stage rows, same rule as RefreshAlterationTotals.
        ClothingPriceText.Text = FormatCurrency(subtotal);
        ClothingSubtotalText.Text = FormatCurrency(subtotal);
        ClothingPreTaxBalanceText.Text = FormatCurrency(money.FinalBase);
        ClothingDepositTaxText.Text = FormatCurrency(money.DepositTax);
        ClothingSumTotalText.Text = FormatCurrency(money.DepositStageTotal);
        ClothingFinalPriceDisplayText.Text = FormatCurrency(subtotal);
        ClothingFinalDownpaymentDisplayText.Text = FormatCurrency(money.Deposit);
        ClothingFinalPreTaxBalanceText.Text = FormatCurrency(money.FinalBase);
        ClothingFinalTotalTaxText.Text = FormatCurrency(money.Tax);
        UpdateTaxBreakdownLines(_clothingControls, money);
        ClothingFinalTotalText.Text = FormatCurrency(money.Total);
        ClothingResidualText.Text = FormatCurrency(residual);
    }

    private void RefreshCustomMadeTotals()
    {
        _customMadeSubtotal = _customMadeRecords.Sum(record => record.Subtotal);
        // See RefreshAlterationTotals: resolves both stage rates and retargets the tax box.
        ApplyStageTaxRates(_customMadeControls);
        var downpayment = ParseDecimalOrZero(CustomMadeDownpaymentBox.Text);
        var money = Order.CalculateSectionPayment(SectionInput(_customMadeControls, _customMadeSubtotal, downpayment));
        _customMadeSumTotal = money.Total;
        _customMadeMoney = money;

        // A cleared balance means nothing is still owed for this section.
        var residual = CustomMadeBalanceClearedCheck.IsChecked.GetValueOrDefault() ? 0m : money.FinalCharge;

        // Deposit-stage rows, same rule as RefreshAlterationTotals.
        CustomMadePriceText.Text = FormatCurrency(_customMadeSubtotal);
        CustomMadeSubtotalText.Text = FormatCurrency(_customMadeSubtotal);
        CustomMadePreTaxBalanceText.Text = FormatCurrency(money.FinalBase);
        CustomMadeDepositTaxText.Text = FormatCurrency(money.DepositTax);
        CustomMadeSumTotalText.Text = FormatCurrency(money.DepositStageTotal);
        CustomMadeFinalPriceDisplayText.Text = FormatCurrency(_customMadeSubtotal);
        CustomMadeFinalDownpaymentDisplayText.Text = FormatCurrency(money.Deposit);
        CustomMadeFinalPreTaxBalanceText.Text = FormatCurrency(money.FinalBase);
        CustomMadeFinalTotalTaxText.Text = FormatCurrency(money.Tax);
        UpdateTaxBreakdownLines(_customMadeControls, money);
        CustomMadeFinalTotalText.Text = FormatCurrency(money.Total);
        CustomMadeResidualText.Text = FormatCurrency(residual);
    }

    private void RefreshAllServicesTotalAmount()
    {
        _totalAmount = _alterationSumTotal + _clothingSumTotal + _customMadeSumTotal;
        TotalAmountText.Text = FormatCurrency(_totalAmount);
        RefreshServicesTotalBreakdown();
        RefreshPaymentSummary();
    }

    // A section's deposit only counts as received once its "deposit received" box is ticked.
    private static decimal SectionReceivedDeposit(SectionPayment money, PaymentSectionControls c)
        => c.DownCompletedCheck.IsChecked is true ? money.ReceivedDownpayment : 0m;

    private void RefreshPaymentSummary()
    {
        var alterationDown = _alterationMoney.Deposit;
        var customMadeDown = _customMadeMoney.Deposit;
        var clothingDown = _clothingMoney.Deposit;

        // Received deposits: nominal deposit plus its card tax, but ONLY for sections whose
        // "deposit received" box has been ticked. Until then the typed amount is what the
        // shop expects to take, not what it holds — mirrors Order.ReceivedDownpayment so the
        // saved order reports the same figure.
        var receivedDownpayment =
            SectionReceivedDeposit(_alterationMoney, _alterationControls)
            + SectionReceivedDeposit(_customMadeMoney, _customMadeControls)
            + SectionReceivedDeposit(_clothingMoney, _clothingControls);

        var alterationCleared = AlterationBalanceClearedCheck.IsChecked.GetValueOrDefault();
        var customMadeCleared = CustomMadeBalanceClearedCheck.IsChecked.GetValueOrDefault();
        var clothingCleared = ClothingBalanceClearedCheck.IsChecked.GetValueOrDefault();

        // Cleared sections no longer contribute to the outstanding final balance.
        var alterationResidual = alterationCleared ? 0m : _alterationMoney.FinalCharge;
        var customMadeResidual = customMadeCleared ? 0m : _customMadeMoney.FinalCharge;
        var clothingResidual = clothingCleared ? 0m : _clothingMoney.FinalCharge;
        var finalBalance = alterationResidual + customMadeResidual + clothingResidual;

        // Received final balance: the taxed final charge collected on every cleared section.
        var receivedFinalBalance =
            (alterationCleared ? _alterationMoney.FinalCharge : 0m)
            + (customMadeCleared ? _customMadeMoney.FinalCharge : 0m)
            + (clothingCleared ? _clothingMoney.FinalCharge : 0m);

        PrepaidDownpaymentText.Text = FormatCurrency(receivedDownpayment);
        SummaryFinalBalanceText.Text = FormatCurrency(finalBalance);
        ReceivedFinalBalanceText.Text = FormatCurrency(receivedFinalBalance);

        // Break down which services still owe an outstanding final balance.
        FinalBalanceBreakdownPanel.Children.Clear();
        AddFinalBalanceDetail("ServiceType.Alterations", alterationResidual);
        AddFinalBalanceDetail("ServiceType.CustomMade", customMadeResidual);
        AddFinalBalanceDetail("ServiceType.ReadyMade", clothingResidual);
        FinalBalanceBreakdownPanel.Visibility = FinalBalanceBreakdownPanel.Children.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        var cleared = IsOrderBalanceCleared();
        UpdateBalanceStatusDisplay(cleared);

        // The "picked up" toggle only becomes selectable once the order has at least one
        // charged service and every final balance is cleared (IsOrderBalanceCleared is
        // false while the order total is zero). Keep it enabled while already ticked so a
        // completed order can still be reverted. Read-only or refunded orders stay locked.
        if (_isReadOnly || _isRefunded)
            PickedUpCheck.IsEnabled = false;
        else
            PickedUpCheck.IsEnabled = cleared || PickedUpCheck.IsChecked.GetValueOrDefault();

        // The master follows the section TICKS, not IsOrderBalanceCleared(). The two disagree
        // exactly when a deposit already covers a section's total: nothing is owed, so the order is
        // financially cleared, but the user may still have unticked the box — and driving the master
        // from the money meant it sprang back on the instant anything recomputed, taking the
        // sections with it. The money question and the checkbox are different questions; only the
        // status display and the picked-up gate below use the money one.
        var previousSync = _syncingPayment;
        _syncingPayment = true;
        ClearAllBalancesCheck.IsChecked = AreAllSectionsMarkedCleared();
        _syncingPayment = previousSync;

        // Requirement 3b: indicate payment types with amount in labeling.
        UpdateMethodLabel(AlterationDownMethodLabel, DownpaymentMethodKey,
            GetSelectedDownMethod(_alterationControls), alterationDown);
        UpdateMethodLabel(AlterationFinalMethodLabel, FinalBalanceMethodKey,
            GetSelectedFinalMethod(_alterationControls), alterationResidual);

        UpdateMethodLabel(CustomMadeDownMethodLabel, DownpaymentMethodKey,
            GetSelectedDownMethod(_customMadeControls), customMadeDown);
        UpdateMethodLabel(CustomMadeFinalMethodLabel, FinalBalanceMethodKey,
            GetSelectedFinalMethod(_customMadeControls), customMadeResidual);

        UpdateMethodLabel(ClothingDownMethodLabel, DownpaymentMethodKey,
            GetSelectedDownMethod(_clothingControls), clothingDown);
        UpdateMethodLabel(ClothingFinalMethodLabel, FinalBalanceMethodKey,
            GetSelectedFinalMethod(_clothingControls), clothingResidual);
    }

    private void UpdateMethodLabel(TextBlock label, string baseKey, PaymentMethod? method, decimal amount)
    {
        var text = _localization[baseKey];
        if (method is not null && method != PaymentMethod.None)
            text += $"  ·  {_localization[$"PaymentMethod.{method}"]}  {FormatCurrency(amount)}";
        label.Text = text;
    }

    // Refunded orders show Payment.Status.Refunded in red; otherwise the settled/outstanding
    // label + green/orange colour.
    private void UpdateBalanceStatusDisplay(bool cleared)
    {
        if (_isRefunded)
        {
            BalanceStatusText.Text = _localization["Payment.Status.Refunded"];
            BalanceStatusText.Foreground = System.Windows.Media.Brushes.Red;
            return;
        }

        BalanceStatusText.Text = cleared
            ? _localization["Payment.Status.Cleared"]
            : _localization["Payment.Status.Outstanding"];
        BalanceStatusText.Foreground = cleared
            ? System.Windows.Media.Brushes.Green
            : System.Windows.Media.Brushes.OrangeRed;
    }

    // Small print under Order.Fields.AllServicesTotalAmount: one line per service that is part of this
    // order, showing what it covers and what it costs, e.g. "Alterations (Garment Adjustments): $123". A service
    // qualifies by carrying order items — the same rule the "clear all balances" pass uses —
    // so a section priced at zero is still listed (flagged) rather than silently dropped.
    private void RefreshServicesTotalBreakdown()
    {
        ServicesTotalBreakdownPanel.Children.Clear();
        AddServiceTotalDetail(_alterationControls, AlterationDetailText());
        AddServiceTotalDetail(_customMadeControls, CustomMadeDetailText());
        AddServiceTotalDetail(_clothingControls, ClothingDetailText());
        ServicesTotalBreakdownPanel.Visibility = ServicesTotalBreakdownPanel.Children.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void AddServiceTotalDetail(PaymentSectionControls c, string detail)
    {
        if (!c.HasItems())
            return;

        var name = _localization[c.ServiceNameKey];
        // Punctuation differs per language (fullwidth in Chinese), so the whole label shape
        // lives in the string table rather than being concatenated here.
        var label = string.IsNullOrEmpty(detail)
            ? _localization.Format("Order.Fields.ServiceTotalLineNoDetail", name)
            : _localization.Format("Order.Fields.ServiceTotalLine", name, detail);

        var missingPrice = c.HasMissingPrice;
        if (missingPrice)
            label += _localization["Order.Fields.ServiceTotalUnpriced"];

        ServicesTotalBreakdownPanel.Children.Add(
            BuildBreakdownRow(label, FormatCurrency(c.SectionTotal()), missingPrice));
    }

    // One breakdown line laid out as label + amount. Its first column joins the summary
    // grid's "SummaryLabel" shared-size group, so the label sits under Order.Fields.AllServicesTotalAmount and the
    // amount under that row's figure.
    private static Grid BuildBreakdownRow(string label, string amount, bool highlight)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
            SharedSizeGroup = "SummaryLabel"
        });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var brush = highlight ? UnpricedLineBrush : BreakdownLineBrush;

        var labelText = new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = brush,
            TextWrapping = TextWrapping.Wrap,
            // Matches the summary labels' right margin so the amounts line up.
            Margin = new Thickness(0, 0, 12, 0)
        };
        Grid.SetColumn(labelText, 0);

        var amountText = new TextBlock
        {
            Text = amount,
            FontSize = 11,
            Foreground = brush
        };
        Grid.SetColumn(amountText, 1);

        row.Children.Add(labelText);
        row.Children.Add(amountText);
        return row;
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

    private void AddFinalBalanceDetail(string serviceKey, decimal residual)
    {
        if (residual <= 0m)
            return;

        FinalBalanceBreakdownPanel.Children.Add(new TextBlock
        {
            Text = $"·  {_localization[serviceKey]}:  {FormatCurrency(residual)}",
            Foreground = System.Windows.Media.Brushes.Firebrick,
            Margin = new Thickness(0, 2, 0, 0)
        });
    }

    // Quick-operation "picked up" toggle: ticking it forces the order status to
    // Completed and locks the status dropdown; unticking reverts the status to
    // Processing and unlocks it. A manual change of the status dropdown to
    // Completed ticks this box in return. A dedicated guard prevents the two
    // handlers from re-triggering each other.
    private void OnPickedUpChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingStatus)
            return;

        // Ask before completing an order that has an unpriced service, and undo the tick if
        // the user backs out. Reverting runs inside the guard so this handler is not re-entered.
        if (PickedUpCheck.IsChecked.GetValueOrDefault() && !ConfirmPickUp())
        {
            _syncingStatus = true;
            try
            {
                PickedUpCheck.IsChecked = false;
            }
            finally
            {
                _syncingStatus = false;
            }
            return;
        }

        _syncingStatus = true;
        try
        {
            if (PickedUpCheck.IsChecked.GetValueOrDefault())
            {
                SelectStatus(OrderStatus.Completed);
                StatusBox.IsEnabled = false;
            }
            else
            {
                SelectStatus(OrderStatus.Processing);
                StatusBox.IsEnabled = true;
            }
        }
        finally
        {
            _syncingStatus = false;
        }
    }

    private void OnStatusChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingStatus)
            return;

        _syncingStatus = true;
        try
        {
            var tag = (StatusBox.SelectedItem as ComboBoxItem)?.Tag as OrderStatus?;
            var isCompleted = tag is OrderStatus.Completed;
            PickedUpCheck.IsChecked = isCompleted;
            StatusBox.IsEnabled = !isCompleted;

            var refunded = tag is OrderStatus.Cancelled or OrderStatus.Returned;
            if (refunded != _isRefunded)
            {
                _isRefunded = refunded;
                ApplyRefundLockState();
            }

            UpdateStatusReasonVisibility();
        }
        finally
        {
            _syncingStatus = false;
        }
    }

    // Shows/hides the return/cancel reason category picker for Cancelled/Returned statuses,
    // swaps its placeholder + label between the return and cancel wording, and defaults the
    // category to the first preset (per the convention: never leave a picker unselected).
    private void UpdateStatusReasonVisibility()
    {
        var tag = (StatusBox.SelectedItem as ComboBoxItem)?.Tag as OrderStatus?;
        var show = tag is OrderStatus.Cancelled or OrderStatus.Returned;

        StatusReasonLabelPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        StatusReasonCategoryBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

        // A reason message must not outlive the reason row. Leaving one behind puts red text under a
        // control that is no longer there, describing a rule that no longer applies.
        if (!show)
        {
            SetFieldError(StatusReasonCategoryErrorText, null);
            SetFieldError(StatusReasonErrorText, null);
        }

        if (show && StatusReasonCategoryBox.SelectedIndex < 0)
            StatusReasonCategoryBox.SelectedIndex = 0;

        StatusReasonHint.Text = _localization[tag == OrderStatus.Cancelled
            ? "OrderEdit.Placeholder.CancelReason"
            : "OrderEdit.Placeholder.ReturnReason"];

        UpdateOtherReasonRowVisibility(show);
    }

    // The free-text "Other" reason row only shows alongside the category picker AND only
    // when the selected preset category is "Other".
    private void UpdateOtherReasonRowVisibility(bool categoryRowVisible)
    {
        var isOther = categoryRowVisible
            && (StatusReasonCategoryBox.SelectedItem as ComboBoxItem)?.Tag as string == OtherStatusReasonTag;

        StatusReasonContainer.Visibility = isOther ? Visibility.Visible : Visibility.Collapsed;
        if (isOther)
            StatusReasonHint.Visibility = string.IsNullOrEmpty(StatusReasonBox.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnStatusReasonCategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        // Picking a category answers the "choose a reason" message, so it goes.
        SetFieldError(StatusReasonCategoryErrorText, null);
        UpdateOtherReasonRowVisibility(StatusReasonCategoryBox.Visibility == Visibility.Visible);
    }

    // Selects the matching preset ComboBoxItem for a loaded order. Legacy records saved
    // before the preset picker existed (or an unrecognized/blank category) fall back to
    // "Other" so their existing free-text StatusReason stays visible and editable.
    private void LoadStatusReasonCategory(string? category)
    {
        var matched = false;
        foreach (var item in StatusReasonCategoryBox.Items.OfType<ComboBoxItem>())
        {
            var isMatch = string.Equals(item.Tag as string, category, StringComparison.Ordinal);
            item.IsSelected = isMatch;
            matched |= isMatch;
        }

        if (!matched)
        {
            foreach (var item in StatusReasonCategoryBox.Items.OfType<ComboBoxItem>())
                item.IsSelected = string.Equals(item.Tag as string, OtherStatusReasonTag, StringComparison.Ordinal);
        }
    }

    private void OnStatusReasonTextChanged(object sender, TextChangedEventArgs e)
        => StatusReasonHint.Visibility = string.IsNullOrEmpty(StatusReasonBox.Text) ? Visibility.Visible : Visibility.Collapsed;

    private void SelectStatus(OrderStatus status)
    {
        foreach (ComboBoxItem item in StatusBox.Items)
        {
            if (item.Tag is OrderStatus tag && tag == status)
            {
                StatusBox.SelectedItem = item;
                break;
            }
        }
    }

    private void OnClearAllBalancesChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingPayment)
            return;

        var clearAll = ClearAllBalancesCheck.IsChecked.GetValueOrDefault();

        _syncingPayment = true;
        try
        {
            ApplyClearAllToSection(clearAll, _alterationControls);
            ApplyClearAllToSection(clearAll, _customMadeControls);
            ApplyClearAllToSection(clearAll, _clothingControls);
        }
        finally
        {
            _syncingPayment = false;
        }

        UpdatePaymentVisibility();
        RefreshComputedTotals(runAutoComplete: false);

        if (clearAll)
            WarnAboutUnpricedServices();
    }

    // Settling every balance marks each participating service BOTH deposit-received and
    // balance-cleared. A service takes part only when it carries order items: an empty
    // section stays out of the payment flow entirely, while a section that has items but no
    // chosen payment method falls back to cash, and one priced at zero still takes part
    // (flagged afterwards, never blocked — a zero price is sometimes deliberate).
    private static void ApplyClearAllToSection(bool clearAll, PaymentSectionControls c)
    {
        if (!clearAll)
        {
            c.BalanceClearedCheck.IsChecked = false;
            return;
        }

        if (!c.HasItems())
            return;

        var downMethod = GetSelectedDownMethod(c);
        if (downMethod is null)
        {
            downMethod = PaymentMethod.Cash;
            SetSelectedDownMethod(c, PaymentMethod.Cash);
        }

        // "None" means no deposit was taken, so there is nothing to confirm as received and
        // the whole charge falls to the final balance.
        var noDeposit = downMethod == PaymentMethod.None;
        if (!noDeposit)
            c.DownCompletedCheck.IsChecked = true;

        // Default the final balance to the deposit method ONLY when the user hasn't already
        // picked one. A manually forced final method (e.g. deposit by card, final by cash)
        // must be respected instead of being reset to the deposit's way.
        if (GetSelectedFinalMethod(c) is null)
            SetSelectedFinalMethod(c, noDeposit ? PaymentMethod.Cash : downMethod);

        c.BalanceClearedCheck.IsChecked = true;
    }

    // Names of every service that carries items but no charge, as a localized list. Empty
    // when every service that takes part is priced.
    private string UnpricedServiceList()
    {
        var unpriced = AllSections
            .Where(c => c.HasMissingPrice)
            .Select(c => _localization[c.ServiceNameKey])
            .ToList();

        return unpriced.Count == 0
            ? string.Empty
            : _localization.JoinList(unpriced);
    }

    // A service carrying items but no charge is allowed — shops zero-rate one on purpose
    // often enough — so this only tells the user, it never blocks settling the order.
    private void WarnAboutUnpricedServices()
    {
        var unpriced = UnpricedServiceList();
        if (unpriced.Length == 0)
            return;

        MessageBox.Show(
            _localization.Format("OrderEdit.Warn.UnpricedServices", unpriced),
            _localization[ValidationTitleKey],
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    // Marking an order picked up completes it, and a completed order opens read-only from then
    // on. That is worth stopping for when a service went out without a charge, so the shop can
    // catch a missing price while the order can still be edited. Returns false to cancel the
    // tick. A fully priced order is not interrupted.
    private bool ConfirmPickUp()
    {
        var unpriced = UnpricedServiceList();
        if (unpriced.Length == 0)
            return true;

        return MessageBox.Show(
            _localization.Format("OrderEdit.Confirm.PickUpUnpriced", unpriced),
            _localization[ValidationTitleKey],
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private bool IsOrderBalanceCleared()
    {
        // A brand-new/empty order starts as outstanding. The gate is order ITEMS, not money:
        // a service priced at zero still takes part, so an order made only of zero-priced
        // items can still be settled (it is flagged as unpriced, not blocked). Gating on
        // _totalAmount here would make the "clear all balances" tick spring straight back off.
        if (!_alterationControls.HasItems() && !_customMadeControls.HasItems() && !_clothingControls.HasItems())
            return false;

        // Cleared only when every charged section is settled; empty sections count as cleared.
        // The deposit is pre-tax, so compare it against the pre-tax subtotal base.
        var alterationCleared = IsSectionCleared(_alterationSubtotal,
            ParseDecimalOrZero(AlterationDownpaymentBox.Text), AlterationBalanceClearedCheck.IsChecked.GetValueOrDefault());
        var customMadeCleared = IsSectionCleared(_customMadeSubtotal,
            ParseDecimalOrZero(CustomMadeDownpaymentBox.Text), CustomMadeBalanceClearedCheck.IsChecked.GetValueOrDefault());
        var clothingCleared = IsSectionCleared(_clothingSubtotal,
            ParseDecimalOrZero(ClothingDownpaymentBox.Text), ClothingBalanceClearedCheck.IsChecked.GetValueOrDefault());
        return alterationCleared && customMadeCleared && clothingCleared;
    }

    private static bool IsSectionCleared(decimal sectionTotal, decimal downpayment, bool balanceCleared)
    {
        if (sectionTotal <= 0m)
            return true;
        if (balanceCleared)
            return true;
        return downpayment >= sectionTotal;
    }

    /// <summary>
    /// Whether every participating section is TICKED as cleared — the master checkbox's own state.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="IsOrderBalanceCleared"/>, which answers a different question: "is
    /// anything still owed". A section whose deposit already covers its total owes nothing, so that
    /// method reports it cleared whatever the tick says — and using it to drive the checkbox made
    /// the checkbox unremovable. This asks only what the user has marked.
    ///
    /// Participation is order ITEMS, matching <see cref="ApplyClearAllToSection"/>, which skips a
    /// section with none: an empty section is not part of the payment flow, and counting it would
    /// leave the master permanently unticked on an order that uses one service.
    /// </remarks>
    private bool AreAllSectionsMarkedCleared()
    {
        var participating = new[] { _alterationControls, _customMadeControls, _clothingControls }
            .Where(section => section.HasItems())
            .ToList();

        return participating.Count > 0
               && participating.TrueForAll(section => section.BalanceClearedCheck.IsChecked is true);
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

    /// <summary>Locks or releases one stage's split: its toggle and every amount in it.</summary>
    private static void SetSplitStageEnabled(PaymentSectionControls c, bool finalStage, bool enabled)
    {
        if (finalStage)
        {
            c.FinalNoSplitRadio.IsEnabled = enabled;
            c.FinalSplitRadio.IsEnabled = enabled;
        }
        else
        {
            c.NoSplitRadio.IsEnabled = enabled;
            c.SplitRadio.IsEnabled = enabled;
        }

        foreach (var row in finalStage ? c.FinalRows : c.DepositRows)
            row.Amount.IsEnabled = enabled;
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

    /// <summary>
    /// Shows or hides the split controls, and answers whether this section is splitting. Kept apart
    /// from the stage visibility above it because they are two questions — WHICH shape the card is in,
    /// and WHERE in the payment flow it has got to — and folding both into one method pushed it past
    /// the complexity limit.
    /// </summary>
    private static void ApplySplitModeVisibility(
        PaymentSectionControls c, bool addedAtSettlement, bool depositSplit, bool finalSplit)
    {
        // Offered only where tax is ADDED at settlement. Where the price already contains it, splitting
        // the tender cannot move a figure on the screen.
        c.SplitToggle.Visibility = Show(addedAtSettlement);

        // One method or several, never both on screen: choosing "Cash" while also allocating money to
        // three types is a contradiction rather than a choice. Each stage hides only ITS OWN method
        // row — the deposit can be a single cash payment while the balance is split three ways.
        c.DownMethodRow.Visibility = Show(!depositSplit);
        c.FinalMethodRow.Visibility = Show(!finalSplit);
    }

    private decimal? GetSubtotalForServiceType(OrderServiceType serviceType)
        => serviceType switch
        {
            OrderServiceType.Alterations => _alterationSubtotal,
            OrderServiceType.ReadyMade => _clothingSubtotal,
            _ => null
        };

    // Feeds the legacy single-rate Orders.TaxRate column, which predates the per-stage
    // split: it carries the deposit rate, matching how the model reads it back
    // (XxxTaxRate ?? TaxRate).
    private decimal? GetTaxRateForServiceType(OrderServiceType serviceType)
        => serviceType switch
        {
            OrderServiceType.Alterations => _alterationControls.DepositTaxRate,
            OrderServiceType.ReadyMade => _clothingControls.DepositTaxRate,
            _ => null
        };

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
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

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

    private UIElement CreateClothingHeader()
    {
        var headerGrid = new Grid { Margin = new Thickness(0, 12, 0, 8) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

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

    private static string GetProposedText(TextBox textBox, string newText)
    {
        var currentText = textBox.Text ?? string.Empty;
        var selectionStart = textBox.SelectionStart;
        var selectionLength = textBox.SelectionLength;
        return currentText.Remove(selectionStart, selectionLength).Insert(selectionStart, newText);
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
