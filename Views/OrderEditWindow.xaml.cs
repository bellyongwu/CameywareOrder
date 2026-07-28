using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    private static readonly Regex EmailPattern =
        new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.None, RegexTimeout);
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

    public OrderEditWindow(IServiceScopeFactory scopeFactory, LocalizationService localization)
    {
        InitializeComponent();
        _scopeFactory = scopeFactory;
        _localization = localization;

        InitializeCommonControls();
        _existing = null;
        _isReadOnly = false;

        // Built from the shop's configured receipt format (本地配置 → 店铺设置). Only a preview:
        // the running number is not reserved until the order is actually saved, so closing this
        // window without saving cannot leave a gap in the shop's receipt run.
        OrderNumberBox.Text = OrderNumberFormatter.Preview(ShopContext.Instance.RequireCurrent(), DateTime.Now);
        CustomerNameBox.Text = string.Empty;
        PhoneNumberBox.Text = string.Empty;
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
        PhoneNumberBox.Text = existing.PhoneNumber;
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
        PhoneNumberBox.IsReadOnly = true;
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
    // and quick-operation checkbox (including 已取货 and 当前服务尾款已结清). Each checkbox's
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
    // to / from 已取消 / 已退货 on an order that is still editable.
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
        RegisterDecimalTextBoxes();
        InitializeCustomMadeRecordsList();
        SelectServiceType(OrderServiceType.Alterations);
        RefreshLocalizedLabels();
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
    }

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

    private bool TryValidateForSave(out OrderStatus status)
    {
        status = default;

        if (string.IsNullOrWhiteSpace(OrderNumberBox.Text))
        {
            ErrorText.Text = _localization["OrderEdit.Validate.OrderNumber"];
            return false;
        }

        if (string.IsNullOrWhiteSpace(CustomerNameBox.Text))
        {
            ErrorText.Text = _localization["OrderEdit.Validate.CustomerName"];
            return false;
        }

        if (string.IsNullOrWhiteSpace(PhoneNumberBox.Text))
        {
            var message = _localization["OrderEdit.Validate.PhoneNumber"];
            ErrorText.Text = message;
            MessageBox.Show(message, _localization[ValidationTitleKey], MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!ValidatePhoneField())
        {
            ErrorText.Text = _localization["OrderEdit.Validate.PhoneInvalid"];
            PhoneNumberBox.Focus();
            return false;
        }

        if (!ValidateEmailField())
        {
            ErrorText.Text = _localization["OrderEdit.Validate.EmailInvalid"];
            EmailBox.Focus();
            return false;
        }

        RefreshComputedTotals();

        if (HasPaymentMethodRequiringEmail() && string.IsNullOrWhiteSpace(EmailBox.Text))
        {
            var emailMessage = _localization["OrderEdit.Validate.EmailRequired"];
            ErrorText.Text = emailMessage;
            MessageBox.Show(emailMessage, _localization[ValidationTitleKey], MessageBoxButton.OK, MessageBoxImage.Warning);
            EmailBox.Focus();
            return false;
        }

        if (_totalAmount < 0)
        {
            ErrorText.Text = _localization["OrderEdit.Validate.TotalAmount"];
            return false;
        }

        if ((StatusBox.SelectedItem as ComboBoxItem)?.Tag is not OrderStatus selectedStatus)
        {
            ErrorText.Text = _localization["OrderEdit.Validate.Status"];
            return false;
        }

        if (selectedStatus == OrderStatus.Shipped && string.IsNullOrWhiteSpace(AddressBox.Text))
        {
            var addressMessage = _localization["OrderEdit.Validate.AddressRequired"];
            ErrorText.Text = addressMessage;
            MessageBox.Show(addressMessage, _localization[ValidationTitleKey], MessageBoxButton.OK, MessageBoxImage.Warning);
            AddressBox.Focus();
            return false;
        }

        if (selectedStatus is OrderStatus.Cancelled or OrderStatus.Returned && !ValidateStatusReason())
            return false;

        status = selectedStatus;
        return true;
    }

    // A cancelled/returned order must always carry a reason: a preset category is required
    // (defaulted so this only fails if somehow cleared), and choosing "Other" additionally
    // requires the free-text detail to be filled in.
    private bool ValidateStatusReason()
    {
        var category = (StatusReasonCategoryBox.SelectedItem as ComboBoxItem)?.Tag as string;
        if (string.IsNullOrWhiteSpace(category))
        {
            var message = _localization["OrderEdit.Validate.StatusReasonRequired"];
            ErrorText.Text = message;
            MessageBox.Show(message, _localization[ValidationTitleKey], MessageBoxButton.OK, MessageBoxImage.Warning);
            StatusReasonCategoryBox.Focus();
            return false;
        }

        if (category == OtherStatusReasonTag && string.IsNullOrWhiteSpace(StatusReasonBox.Text))
        {
            var message = _localization["OrderEdit.Validate.StatusReasonOtherRequired"];
            ErrorText.Text = message;
            MessageBox.Show(message, _localization[ValidationTitleKey], MessageBoxButton.OK, MessageBoxImage.Warning);
            StatusReasonBox.Focus();
            return false;
        }

        return true;
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

        db.OrderItems.RemoveRange(order.Items);
        order.Items.Clear();
        foreach (var clothingItem in data.ClothingItems)
            order.Items.Add(clothingItem);
    }

    private void ApplyEditableFields(Order order, OrderSaveData data)
    {
        order.CustomerName = CustomerNameBox.Text.Trim();
        order.PhoneNumber = PhoneNumberBox.Text.Trim();
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
        order.Notes = NullIfWhiteSpace(NotesBox.Text);
        order.LastModifiedDate = DateTime.UtcNow;
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

        MessageBox.Show(
            _localization["OrderEdit.Validate.EmailRequired"],
            _localization[ValidationTitleKey],
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        EmailBox.Focus();
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
            defaultPhoneNumber: PhoneNumberBox.Text,
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
            defaultPhoneNumber: PhoneNumberBox.Text,
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

    private void OnPhoneNumberBoxLostFocus(object sender, RoutedEventArgs e) => ValidatePhoneField();

    // Requirement 5b - an entered email must be well formed. Empty stays allowed
    // here because the payment flow separately enforces email for e-transfer.
    private bool ValidateEmailField()
    {
        var email = EmailBox.Text?.Trim() ?? string.Empty;
        var valid = email.Length == 0 || EmailPattern.IsMatch(email);
        SetFieldError(EmailErrorText, valid ? null : _localization["OrderEdit.Validate.EmailInvalid"]);
        return valid;
    }

    // Requirement 5c: common phone validation — optional leading +, digits and
    // separators only, with 7-15 actual digits.
    private bool ValidatePhoneField()
    {
        var phone = PhoneNumberBox.Text?.Trim() ?? string.Empty;
        var valid = phone.Length == 0 || IsValidPhone(phone);
        SetFieldError(PhoneErrorText, valid ? null : _localization["OrderEdit.Validate.PhoneInvalid"]);
        return valid;
    }

    private static bool IsValidPhone(string phone)
    {
        if (!Regex.IsMatch(phone, @"^\+?[\d\s\-().]+$", RegexOptions.None, RegexTimeout))
            return false;

        var digits = phone.Count(char.IsDigit);
        return digits is >= 7 and <= 15;
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

    private bool CanOpenCustomMadeWindow()
    {
        if (string.IsNullOrWhiteSpace(CustomerNameBox.Text))
        {
            MessageBox.Show(_localization["OrderEdit.Validate.CustomerName"], _localization[ValidationTitleKey], MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (string.IsNullOrWhiteSpace(PhoneNumberBox.Text))
        {
            MessageBox.Show(_localization["OrderEdit.Validate.PhoneNumber"], _localization[ValidationTitleKey], MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return true;
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
            // Deposit received covers the full total: mirror the method and mark cleared.
            SetSelectedFinalMethod(c, downMethod);
            if (c.BalanceClearedCheck.IsChecked is not true)
                c.BalanceClearedCheck.IsChecked = true;
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
    /// what makes a change in 店铺设置 take effect across the shop. The one exception is a
    /// read-only order — completed, shipped, cancelled or returned. That one keeps the rates it
    /// was actually charged, because its receipt has already been printed and the screen must not
    /// disagree with the paper.
    /// </summary>
    private void ApplyStageTaxRates(PaymentSectionControls c)
    {
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

    private static string FormatTaxRate(decimal ratePercent) => $"{ratePercent:0.##}%";

    // Small print under 此服务总计税: how the section's tax splits across the two portions
    // and which method settled each, so a $0 line reads as "that portion wasn't card"
    // rather than as a missing charge.
    private void UpdateTaxBreakdownLines(PaymentSectionControls c, SectionPayment money)
    {
        var depositMethod = GetSelectedDownMethod(c);
        c.DepositTaxLine.Text = _localization.Format("Order.Fields.DepositTaxLine",
            PaymentMethodName(depositMethod),
            FormatCurrency(money.ReceivedDownpayment - money.Deposit));
        c.FinalTaxLine.Text = _localization.Format("Order.Fields.FinalTaxLine",
            PaymentMethodName(EffectiveFinalMethod(c)),
            FormatCurrency(money.FinalCharge - money.FinalBase));
    }

    // Normalized so an order still carrying the legacy single "Card" value reads as Debit Card
    // rather than the retired label.
    private string PaymentMethodName(PaymentMethod? method)
        => _localization[$"PaymentMethod.{PaymentTaxRules.Normalize(method ?? PaymentMethod.None)}"];

    // Names the stage the tax box is editing, so a rate typed here is never mistaken for
    // the other portion's rate.
    private void UpdateTaxLabel(PaymentSectionControls c)
        => c.TaxLabel.Text = _localization[c.ShowingFinalRate
            ? "Order.Fields.FinalTaxRate"
            : "Order.Fields.DepositTaxRate"];

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
        var money = Order.CalculateSectionPayment(price, downpayment,
            _alterationControls.DepositTaxRate, _alterationControls.FinalTaxRate,
            GetSelectedDownMethod(_alterationControls),
            EffectiveFinalMethod(_alterationControls));
        // A cleared balance means nothing is still owed for this section.
        var residual = AlterationBalanceClearedCheck.IsChecked.GetValueOrDefault() ? 0m : money.FinalCharge;

        _alterationSubtotal = price;
        _alterationSumTotal = money.Total;
        _alterationMoney = money;

        // Deposit-stage rows are scoped to that stage and add up: subtotal + deposit tax.
        var alterationStageTax = DepositStageTax(money);
        AlterationSubtotalText.Text = FormatCurrency(price);
        // Pre-tax balance still to come: the subtotal less the deposit, before any card tax.
        AlterationPreTaxBalanceText.Text = FormatCurrency(money.FinalBase);
        AlterationDepositTaxText.Text = FormatCurrency(alterationStageTax);
        AlterationSumTotalText.Text = FormatCurrency(money.Subtotal + alterationStageTax);
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
        var money = Order.CalculateSectionPayment(subtotal, downpayment,
            _clothingControls.DepositTaxRate, _clothingControls.FinalTaxRate,
            GetSelectedDownMethod(_clothingControls),
            EffectiveFinalMethod(_clothingControls));
        // A cleared balance means nothing is still owed for this section.
        var residual = ClothingBalanceClearedCheck.IsChecked.GetValueOrDefault() ? 0m : money.FinalCharge;

        _clothingSubtotal = subtotal;
        _clothingSumTotal = money.Total;
        _clothingMoney = money;

        // Deposit-stage rows, same rule as RefreshAlterationTotals.
        var clothingStageTax = DepositStageTax(money);
        ClothingPriceText.Text = FormatCurrency(subtotal);
        ClothingSubtotalText.Text = FormatCurrency(subtotal);
        ClothingPreTaxBalanceText.Text = FormatCurrency(money.FinalBase);
        ClothingDepositTaxText.Text = FormatCurrency(clothingStageTax);
        ClothingSumTotalText.Text = FormatCurrency(money.Subtotal + clothingStageTax);
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
        var money = Order.CalculateSectionPayment(_customMadeSubtotal, downpayment,
            _customMadeControls.DepositTaxRate, _customMadeControls.FinalTaxRate,
            GetSelectedDownMethod(_customMadeControls),
            EffectiveFinalMethod(_customMadeControls));
        _customMadeSumTotal = money.Total;
        _customMadeMoney = money;

        // A cleared balance means nothing is still owed for this section.
        var residual = CustomMadeBalanceClearedCheck.IsChecked.GetValueOrDefault() ? 0m : money.FinalCharge;

        // Deposit-stage rows, same rule as RefreshAlterationTotals.
        var customMadeStageTax = DepositStageTax(money);
        CustomMadePriceText.Text = FormatCurrency(_customMadeSubtotal);
        CustomMadeSubtotalText.Text = FormatCurrency(_customMadeSubtotal);
        CustomMadePreTaxBalanceText.Text = FormatCurrency(money.FinalBase);
        CustomMadeDepositTaxText.Text = FormatCurrency(customMadeStageTax);
        CustomMadeSumTotalText.Text = FormatCurrency(money.Subtotal + customMadeStageTax);
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

    // Tax committed at the DEPOSIT stage: the deposit portion's tax alone. The final
    // portion's tax joins only at the final stage, whose panel shows the complete
    // deposit + final figure. Keeping this row stage-scoped is what makes the deposit amount
    // visibly move it — a section's TOTAL tax is invariant to the deposit split whenever both
    // portions share a rate (deposit*r + (subtotal-deposit)*r == subtotal*r), so showing the
    // total here made the row look frozen.
    private static decimal DepositStageTax(SectionPayment money)
        => money.ReceivedDownpayment - money.Deposit;

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

        // Keep the master "clear all balances" checkbox in sync with the overall state
        // without re-triggering its handler.
        var previousSync = _syncingPayment;
        _syncingPayment = true;
        ClearAllBalancesCheck.IsChecked = cleared;
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

    // Refunded orders show 已退款或部分退款 in red; otherwise the settled/outstanding
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

    // Small print under 全部服务总金额: one line per service that is part of this order,
    // showing what it covers and what it costs, e.g. "修改衣服（服装修改）：$123". A service
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
    // grid's "SummaryLabel" shared-size group, so the label sits under 全部服务总金额 and the
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
    // resolved through the same reader the main list's 定制服务 column uses.
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
        => UpdateOtherReasonRowVisibility(StatusReasonCategoryBox.Visibility == Visibility.Visible);

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

    private void UpdatePaymentVisibility()
    {
        UpdateSectionVisibility(_alterationControls);
        UpdateSectionVisibility(_customMadeControls);
        UpdateSectionVisibility(_clothingControls);

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
        c.DownCompletedCheck.IsEnabled = !sectionLocked;
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
    }

    private static void UpdateSectionVisibility(PaymentSectionControls c)
    {
        var anyDownSelected = c.DownNone.IsChecked is true || c.DownEtransfer.IsChecked is true
            || c.DownDebit.IsChecked is true || c.DownCredit.IsChecked is true || c.DownCash.IsChecked is true;
        c.PricingPanel.Visibility = anyDownSelected ? Visibility.Visible : Visibility.Collapsed;

        var isNone = c.DownNone.IsChecked is true;
        c.DownCompletedCheck.Visibility = isNone ? Visibility.Collapsed : Visibility.Visible;

        if (isNone)
        {
            if (c.DownpaymentBox.Text != "0")
                c.DownpaymentBox.Text = "0";
            c.DownpaymentBox.IsEnabled = false;
        }
        else
        {
            c.DownpaymentBox.IsEnabled = true;
        }

        var depositCompleted = c.DownCompletedCheck.IsChecked is true;
        c.FinalBlock.Visibility = (isNone || depositCompleted)
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Deposit breakdown: visible whenever a payment method is chosen and deposit is not yet received.
        // This covers both "normal" deposit flow and the DownNone case so tax/total are always visible.
        c.DepositBreakdownPanel.Visibility = (anyDownSelected && !depositCompleted)
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Final breakdown: visible only when deposit is explicitly marked received (inside FinalBlock).
        c.FinalBreakdownPanel.Visibility = depositCompleted
            ? Visibility.Visible
            : Visibility.Collapsed;
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

        // From the SHOP's catalogue, not a fixed list: 本地配置 → 商品类别 edits it per branch.
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

    private static string FormatCurrency(decimal amount)
    {
        var symbol = Services.CurrencySettingService.Instance.Symbol;
        return $"{symbol}{amount:0.00}";
    }

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
