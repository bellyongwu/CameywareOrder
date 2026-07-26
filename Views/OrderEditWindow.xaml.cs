using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LeeYongeOrdering.Data;
using LeeYongeOrdering.Localization;
using LeeYongeOrdering.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LeeYongeOrdering.Views;

public partial class OrderEditWindow : Window
{
    private const decimal DefaultTaxRate = 13m;
    private const string EditTitleKey = "OrderEdit.EditTitle";
    private const string ViewTitleKey = "OrderEdit.ViewTitle";
    private const string DownpaymentMethodKey = "OrderEdit.DownpaymentMethod";
    private const string FinalBalanceMethodKey = "OrderEdit.FinalBalanceMethod";
    private const string ValidationTitleKey = "OrderEdit.ValidationTitle";
    private static readonly Regex DecimalInputPattern = new("^\\d*(\\.\\d{0,2})?$");
    private static readonly Regex EmailPattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");
    private static readonly string[] ClothingItemKeys =
    {
        "Jackets",
        "TiesBowtie",
        "Qipao",
        "LeatherShoes",
        "Other"
    };

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
        public required RadioButton DownCard { get; init; }
        public required RadioButton DownCash { get; init; }
        public required TextBox DownpaymentBox { get; init; }
        public required CheckBox DownCompletedCheck { get; init; }
        public required RadioButton FinalEtransfer { get; init; }
        public required RadioButton FinalCard { get; init; }
        public required RadioButton FinalCash { get; init; }
        public required CheckBox BalanceClearedCheck { get; init; }
        public required UIElement PricingPanel { get; init; }
        public required UIElement FinalBlock { get; init; }
        // Deposit-stage breakdown panel (tax on deposit + post-tax total; hidden once deposit received).
        public required StackPanel DepositBreakdownPanel { get; init; }
        // Final-stage complete breakdown (shown inside FinalBlock once deposit is received).
        public required StackPanel FinalBreakdownPanel { get; init; }
        // Tax-rate input for this section (owned here so locking is fully centralized).
        public required TextBox TaxBox { get; init; }
        // True when any payment portion of this section uses a card method (drives tax applicability).
        public bool CardUsed => DownCard.IsChecked is true || FinalCard.IsChecked is true;
    }

    public OrderEditWindow(IServiceScopeFactory scopeFactory, LocalizationService localization)
    {
        InitializeComponent();
        _scopeFactory = scopeFactory;
        _localization = localization;

        InitializeCommonControls();
        _existing = null;
        _isReadOnly = false;

        OrderNumberBox.Text = $"ORD-{DateTime.Now:yyyyMMdd-HHmmss}";
        CustomerNameBox.Text = string.Empty;
        PhoneNumberBox.Text = string.Empty;
        EmailBox.Text = string.Empty;
        AddressBox.Text = string.Empty;
        StatusReasonBox.Text = string.Empty;
        StatusReasonCategoryBox.SelectedIndex = 0;
        TotalAmountText.Text = FormatCurrency(0m);
        AlterationTaxBox.Text = DefaultTaxRate.ToString("0.##");
        ClothingTaxBox.Text = DefaultTaxRate.ToString("0.##");
        CustomMadeTaxBox.Text = DefaultTaxRate.ToString("0.##");
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
        if (!matchedCategory)
            AlterationCategoryBox.SelectedIndex = 0;
        AlterationAdditionalNotesBox.Text = existing.AdditionalNotes;
        AlterationPriceBox.Text = (existing.AlterationSubtotal ?? existing.Subtotal)?.ToString("0.##") ?? string.Empty;
        AlterationTaxBox.Text = (existing.AlterationTaxRate ?? existing.TaxRate)?.ToString("0.##") ?? DefaultTaxRate.ToString("0.##");
        ClothingTaxBox.Text = (existing.ClothingTaxRate ?? existing.TaxRate)?.ToString("0.##") ?? DefaultTaxRate.ToString("0.##");
        CustomMadeTaxBox.Text = existing.CustomMadeTaxRate?.ToString("0.##") ?? DefaultTaxRate.ToString("0.##");

        if (existing.ServiceType == OrderServiceType.Alterations && string.IsNullOrWhiteSpace(AlterationPriceBox.Text))
        {
            var effectiveTaxRate = ParseDecimalOrZero(AlterationTaxBox.Text);
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

    private static bool IsReadOnlyStatus(OrderStatus status)
        => status is OrderStatus.Completed or OrderStatus.Cancelled or OrderStatus.Returned;

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
        AlterationTaxBox.IsReadOnly = true;
        CustomMadeTaxBox.IsReadOnly = true;
        ClothingTaxBox.IsReadOnly = true;
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
        section.DownCard.IsEnabled = enabled;
        section.DownCash.IsEnabled = enabled;
        section.DownCompletedCheck.IsEnabled = enabled;
        section.FinalEtransfer.IsEnabled = enabled;
        section.FinalCard.IsEnabled = enabled;
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
        AlterationTaxBox.IsReadOnly = !enabled;
        CustomMadeTaxBox.IsReadOnly = !enabled;
        ClothingTaxBox.IsReadOnly = !enabled;

        AddItemButton.IsEnabled = enabled;
        AddCustomMadeButton.IsEnabled = enabled;
        RemoveCustomMadeButton.IsEnabled = enabled;
        ClearAllBalancesCheck.IsEnabled = enabled;
        SetClothingRowsLocked(!enabled);
    }

    // Applies / removes the red strikethrough "not applicable" styling on every service
    // and quick-operation checkbox (including 已取货 and 当前服务尾款已结清).
    private void ApplyNotApplicableCheckboxStyle(bool notApplicable)
    {
        var style = notApplicable ? (Style)FindResource("NotApplicableCheckBox") : null;
        ClearAllBalancesCheck.Style = style;
        PickedUpCheck.Style = style;
        AlterationDownCompletedCheck.Style = style;
        AlterationBalanceClearedCheck.Style = style;
        CustomMadeDownCompletedCheck.Style = style;
        CustomMadeBalanceClearedCheck.Style = style;
        ClothingDownCompletedCheck.Style = style;
        ClothingBalanceClearedCheck.Style = style;
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
            DownCard = AlterationDownCard,
            DownCash = AlterationDownCash,
            DownpaymentBox = AlterationDownpaymentBox,
            DownCompletedCheck = AlterationDownCompletedCheck,
            FinalEtransfer = AlterationFinalEtransfer,
            FinalCard = AlterationFinalCard,
            FinalCash = AlterationFinalCash,
            BalanceClearedCheck = AlterationBalanceClearedCheck,
            PricingPanel = AlterationPricingPanel,
            FinalBlock = AlterationFinalBlock,
            DepositBreakdownPanel = AlterationDepositBreakdownPanel,
            FinalBreakdownPanel = AlterationFinalBreakdownPanel,
            TaxBox = AlterationTaxBox
        };
        _customMadeControls = new PaymentSectionControls
        {
            DownNone = CustomMadeDownNone,
            DownEtransfer = CustomMadeDownEtransfer,
            DownCard = CustomMadeDownCard,
            DownCash = CustomMadeDownCash,
            DownpaymentBox = CustomMadeDownpaymentBox,
            DownCompletedCheck = CustomMadeDownCompletedCheck,
            FinalEtransfer = CustomMadeFinalEtransfer,
            FinalCard = CustomMadeFinalCard,
            FinalCash = CustomMadeFinalCash,
            BalanceClearedCheck = CustomMadeBalanceClearedCheck,
            PricingPanel = CustomMadePricingPanel,
            FinalBlock = CustomMadeFinalBlock,
            DepositBreakdownPanel = CustomMadeDepositBreakdownPanel,
            FinalBreakdownPanel = CustomMadeFinalBreakdownPanel,
            TaxBox = CustomMadeTaxBox
        };
        _clothingControls = new PaymentSectionControls
        {
            DownNone = ClothingDownNone,
            DownEtransfer = ClothingDownEtransfer,
            DownCard = ClothingDownCard,
            DownCash = ClothingDownCash,
            DownpaymentBox = ClothingDownpaymentBox,
            DownCompletedCheck = ClothingDownCompletedCheck,
            FinalEtransfer = ClothingFinalEtransfer,
            FinalCard = ClothingFinalCard,
            FinalCash = ClothingFinalCash,
            BalanceClearedCheck = ClothingBalanceClearedCheck,
            PricingPanel = ClothingPricingPanel,
            FinalBlock = ClothingFinalBlock,
            DepositBreakdownPanel = ClothingDepositBreakdownPanel,
            FinalBreakdownPanel = ClothingFinalBreakdownPanel,
            TaxBox = ClothingTaxBox
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
        var viewOnly = _isReadOnly || CustomMadeBalanceClearedCheck.IsChecked is true;
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

            if (_existing is null)
                AddNewOrder(db, data);
            else
                await UpdateExistingOrderAsync(db, data);

            await db.SaveChangesAsync();
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

        if (category == "Other" && string.IsNullOrWhiteSpace(StatusReasonBox.Text))
        {
            var message = _localization["OrderEdit.Validate.StatusReasonOtherRequired"];
            ErrorText.Text = message;
            MessageBox.Show(message, _localization[ValidationTitleKey], MessageBoxButton.OK, MessageBoxImage.Warning);
            StatusReasonBox.Focus();
            return false;
        }

        return true;
    }

    private void AddNewOrder(AppDbContext db, OrderSaveData data)
    {
        var newOrder = new Order
        {
            OrderNumber = OrderNumberBox.Text.Trim(),
            OrderDate = DateTime.UtcNow,
            Items = data.ClothingItems
        };
        ApplyEditableFields(newOrder, data);
        db.Orders.Add(newOrder);
    }

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
        order.StatusReason = category == "Other" ? NullIfWhiteSpace(StatusReasonBox.Text) : null;
    }

    private void OnServiceTypeChanged(object sender, RoutedEventArgs e)
    {
        RefreshServicePanels();
        RefreshComputedTotals();
    }

    private void OnAlterationValuesChanged(object sender, TextChangedEventArgs e)
        => RefreshComputedTotals(runAutoComplete: false);

    private void OnClothingValuesChanged(object sender, TextChangedEventArgs e)
        => RefreshComputedTotals(runAutoComplete: false);

    private void OnCustomMadeValuesChanged(object sender, TextChangedEventArgs e)
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

        RefreshComputedTotals();
    }

    private void OnPaymentOptionChanged(object sender, RoutedEventArgs e)
    {
        // Ignore programmatic changes made while syncing payment state.
        if (_syncingPayment)
            return;

        // When a deposit method radio is selected, always clear the deposit-received flag.
        // Only zero the deposit amount when "None" is chosen — switching between real methods
        // (Cash / Card / Etransfer) preserves the entered amount so the breakdown stays accurate.
        if (sender is RadioButton radio
            && TryGetDownMethodResetTargets(radio, out var downpaymentBox, out var completedCheck, out var isNoneMethod))
        {
            _syncingPayment = true;
            try
            {
                if (isNoneMethod && downpaymentBox.Text != "0")
                    downpaymentBox.Text = "0";
                completedCheck.IsChecked = false;
            }
            finally
            {
                _syncingPayment = false;
            }
        }

        WarnIfEmailMissing(sender);
        UpdatePaymentVisibility();
        RefreshComputedTotals();
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

    private bool TryGetDownMethodResetTargets(RadioButton radio, out TextBox downpaymentBox, out CheckBox completedCheck, out bool isNoneMethod)
    {
        if (radio == AlterationDownCash || radio == AlterationDownCard || radio == AlterationDownEtransfer || radio == AlterationDownNone)
        {
            downpaymentBox = AlterationDownpaymentBox;
            completedCheck = AlterationDownCompletedCheck;
            isNoneMethod = radio == AlterationDownNone;
            return true;
        }
        if (radio == CustomMadeDownCash || radio == CustomMadeDownCard || radio == CustomMadeDownEtransfer || radio == CustomMadeDownNone)
        {
            downpaymentBox = CustomMadeDownpaymentBox;
            completedCheck = CustomMadeDownCompletedCheck;
            isNoneMethod = radio == CustomMadeDownNone;
            return true;
        }
        if (radio == ClothingDownCash || radio == ClothingDownCard || radio == ClothingDownEtransfer || radio == ClothingDownNone)
        {
            downpaymentBox = ClothingDownpaymentBox;
            completedCheck = ClothingDownCompletedCheck;
            isNoneMethod = radio == ClothingDownNone;
            return true;
        }

        downpaymentBox = null!;
        completedCheck = null!;
        isNoneMethod = false;
        return false;
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
        RefreshClothingTotals();
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
        var recordReadOnly = _isReadOnly || CustomMadeBalanceClearedCheck.IsChecked is true;

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
        if (!Regex.IsMatch(phone, @"^\+?[\d\s\-().]+$"))
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
        RegisterDecimalTextBox(AlterationPriceBox);
        RegisterDecimalTextBox(AlterationTaxBox);
        RegisterDecimalTextBox(AlterationDownpaymentBox);
        RegisterDecimalTextBox(CustomMadeTaxBox);
        RegisterDecimalTextBox(ClothingTaxBox);
        RegisterDecimalTextBox(ClothingDownpaymentBox);
        RegisterDecimalTextBox(CustomMadeDownpaymentBox);

        RegisterDepositBox(AlterationDownpaymentBox);
        RegisterDepositBox(CustomMadeDownpaymentBox);
        RegisterDepositBox(ClothingDownpaymentBox);
    }

    private void RegisterDepositBox(TextBox box)
    {
        box.GotFocus += OnDepositBoxGotFocus;
        box.LostFocus += OnDepositBoxLostFocus;
    }

    // Requirement 4c - clearing the leading zero on entry avoids malformed numeric
    // entries. Leaving the box empty or invalid restores a valid zero on exit.
    private void OnDepositBoxGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box || !box.IsEnabled)
            return;

        if (box.Text.Length > 0 && ParseDecimalOrZero(box.Text) == 0m)
        {
            _syncingPayment = true;
            box.Clear();
            _syncingPayment = false;
        }
        box.SelectAll();
    }

    private void OnDepositBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box)
            return;

        if (string.IsNullOrWhiteSpace(box.Text) || !decimal.TryParse(box.Text, out _))
        {
            _syncingPayment = true;
            box.Text = "0";
            _syncingPayment = false;
            RefreshComputedTotals();
        }
    }

    private void RegisterDecimalTextBox(TextBox textBox)
    {
        DataObject.AddPastingHandler(textBox, OnDecimalTextBoxPaste);
    }

    private void OnDecimalTextBoxPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        var proposedText = GetProposedText(textBox, e.Text);
        e.Handled = !DecimalInputPattern.IsMatch(proposedText);
    }

    private void OnDecimalTextBoxPaste(object sender, DataObjectPastingEventArgs e)
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

    // Centralized lock manager for a single payment section's input controls.
    // All IsReadOnly decisions live here; no Refresh*Totals method touches lock state.
    private void ApplySectionInputLocks(PaymentSectionControls c, TextBox? priceBox)
    {
        var downCompleted = c.DownCompletedCheck.IsChecked is true;
        var sectionLocked = _isReadOnly || _isRefunded || c.BalanceClearedCheck.IsChecked is true;
        var inputsLocked = sectionLocked || downCompleted;

        if (priceBox is not null)
            priceBox.IsReadOnly = inputsLocked;

        // Tax box: read-only when no card is in use (not applicable), or when inputs are locked.
        c.TaxBox.IsReadOnly = !c.CardUsed || inputsLocked;
        c.DownpaymentBox.IsReadOnly = inputsLocked;
    }

    private void RefreshPricingLocks()
    {
        ApplySectionInputLocks(_alterationControls, AlterationPriceBox);
        ApplySectionInputLocks(_customMadeControls, priceBox: null);
        ApplySectionInputLocks(_clothingControls, priceBox: null);

        // Section-level controls not captured inside PaymentSectionControls.
        var customMadeSectionLocked = _isReadOnly || _isRefunded || CustomMadeBalanceClearedCheck.IsChecked is true;
        AddCustomMadeButton.IsEnabled = !customMadeSectionLocked;
        RemoveCustomMadeButton.IsEnabled = !customMadeSectionLocked;
        RefreshCustomMadeButtonLabel();

        var clothingSectionLocked = _isReadOnly || _isRefunded || ClothingBalanceClearedCheck.IsChecked is true;
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
        var downMethod = GetSelectedDownMethod(c.DownNone, c.DownEtransfer, c.DownCard, c.DownCash);
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
            SetSelectedPaymentMethod(c.FinalEtransfer, c.FinalCard, c.FinalCash, downMethod);
            if (c.BalanceClearedCheck.IsChecked is not true)
                c.BalanceClearedCheck.IsChecked = true;
            return true;
        }

        // Deposit no longer covers the total (or deposit-received was unchecked):
        // reinitialize only what we auto-filled. The deposit-received checkbox stays manual.
        if (wasAutoCompleted)
        {
            SetSelectedPaymentMethod(c.FinalEtransfer, c.FinalCard, c.FinalCash, null);
            c.BalanceClearedCheck.IsChecked = false;
        }

        // Bug 1: once the deposit is marked received, default the final method to mirror
        // the deposit method until the user changes it.
        if (hasRealDownMethod && depositReceived
            && GetSelectedPaymentMethod(c.FinalEtransfer, c.FinalCard, c.FinalCash) is null)
        {
            SetSelectedPaymentMethod(c.FinalEtransfer, c.FinalCard, c.FinalCash, downMethod);
        }

        return false;
    }

    // Business rule: Cash and Etransfer are always taxed at 0%; only Card is taxable.
    // Forces the tax box text to reflect the effective rate (so the displayed value never
    // lies about what's actually charged) and returns the rate to use in the calculation.
    private decimal ApplyTaxRateRule(TextBox taxBox, bool cardUsed)
    {
        if (!cardUsed)
        {
            if (taxBox.Text != "0")
                taxBox.Text = "0";
            return 0m;
        }

        // First time card is selected for this section, default to the standard rate.
        if (ParseDecimalOrZero(taxBox.Text) == 0m)
            taxBox.Text = DefaultTaxRate.ToString("0.##");

        return ParseDecimalOrZero(taxBox.Text);
    }

    private void RefreshAlterationTotals()
    {
        var price = ParseDecimalOrZero(AlterationPriceBox.Text);
        var cardUsed = _alterationControls.CardUsed;
        // Rule: Cash/Etransfer are always 0% tax; Card defaults to the standard rate the
        // first time it's selected. The box is forced back to "0" whenever no leg uses
        // card, so the displayed rate always matches what is actually charged.
        var taxRate = ApplyTaxRateRule(AlterationTaxBox, cardUsed);
        var downpayment = ParseDecimalOrZero(AlterationDownpaymentBox.Text);
        var money = Order.CalculateSectionPayment(price, downpayment, taxRate,
            GetSelectedDownMethod(AlterationDownNone, AlterationDownEtransfer, AlterationDownCard, AlterationDownCash),
            GetSelectedPaymentMethod(AlterationFinalEtransfer, AlterationFinalCard, AlterationFinalCash));
        // A cleared balance means nothing is still owed for this section.
        var residual = AlterationBalanceClearedCheck.IsChecked.GetValueOrDefault() ? 0m : money.FinalCharge;

        _alterationSubtotal = price;
        _alterationSumTotal = money.Total;
        _alterationMoney = money;

        AlterationSubtotalText.Text = FormatCurrency(price);
        AlterationDepositTaxText.Text = FormatCurrency(money.ReceivedDownpayment - money.Deposit);
        AlterationSumTotalText.Text = FormatCurrency(money.Total);
        AlterationFinalPriceDisplayText.Text = FormatCurrency(price);
        AlterationFinalDownpaymentDisplayText.Text = FormatCurrency(money.Deposit);
        AlterationFinalTotalTaxText.Text = FormatCurrency(money.Tax);
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

        var cardUsed = _clothingControls.CardUsed;
        // Rule: Cash/Etransfer are always 0% tax; Card defaults to the standard rate the
        // first time it's selected.
        var taxRate = ApplyTaxRateRule(ClothingTaxBox, cardUsed);
        var downpayment = ParseDecimalOrZero(ClothingDownpaymentBox.Text);
        var money = Order.CalculateSectionPayment(subtotal, downpayment, taxRate,
            GetSelectedDownMethod(ClothingDownNone, ClothingDownEtransfer, ClothingDownCard, ClothingDownCash),
            GetSelectedPaymentMethod(ClothingFinalEtransfer, ClothingFinalCard, ClothingFinalCash));
        // A cleared balance means nothing is still owed for this section.
        var residual = ClothingBalanceClearedCheck.IsChecked.GetValueOrDefault() ? 0m : money.FinalCharge;

        _clothingSubtotal = subtotal;
        _clothingSumTotal = money.Total;
        _clothingMoney = money;

        ClothingPriceText.Text = FormatCurrency(subtotal);
        ClothingSubtotalText.Text = FormatCurrency(subtotal);
        ClothingDepositTaxText.Text = FormatCurrency(money.ReceivedDownpayment - money.Deposit);
        ClothingSumTotalText.Text = FormatCurrency(money.Total);
        ClothingFinalPriceDisplayText.Text = FormatCurrency(subtotal);
        ClothingFinalDownpaymentDisplayText.Text = FormatCurrency(money.Deposit);
        ClothingFinalTotalTaxText.Text = FormatCurrency(money.Tax);
        ClothingFinalTotalText.Text = FormatCurrency(money.Total);
        ClothingResidualText.Text = FormatCurrency(residual);
    }

    private void RefreshCustomMadeTotals()
    {
        _customMadeSubtotal = _customMadeRecords.Sum(record => record.Subtotal);
        var cardUsed = _customMadeControls.CardUsed;
        // Rule: Cash/Etransfer are always 0% tax; Card defaults to the standard rate the
        // first time it's selected.
        var taxRate = ApplyTaxRateRule(CustomMadeTaxBox, cardUsed);
        var downpayment = ParseDecimalOrZero(CustomMadeDownpaymentBox.Text);
        var money = Order.CalculateSectionPayment(_customMadeSubtotal, downpayment, taxRate,
            GetSelectedDownMethod(CustomMadeDownNone, CustomMadeDownEtransfer, CustomMadeDownCard, CustomMadeDownCash),
            GetSelectedPaymentMethod(CustomMadeFinalEtransfer, CustomMadeFinalCard, CustomMadeFinalCash));
        _customMadeSumTotal = money.Total;
        _customMadeMoney = money;

        // A cleared balance means nothing is still owed for this section.
        var residual = CustomMadeBalanceClearedCheck.IsChecked.GetValueOrDefault() ? 0m : money.FinalCharge;

        CustomMadePriceText.Text = FormatCurrency(_customMadeSubtotal);
        CustomMadeSubtotalText.Text = FormatCurrency(_customMadeSubtotal);
        CustomMadeDepositTaxText.Text = FormatCurrency(money.ReceivedDownpayment - money.Deposit);
        CustomMadeSumTotalText.Text = FormatCurrency(money.Total);
        CustomMadeFinalPriceDisplayText.Text = FormatCurrency(_customMadeSubtotal);
        CustomMadeFinalDownpaymentDisplayText.Text = FormatCurrency(money.Deposit);
        CustomMadeFinalTotalTaxText.Text = FormatCurrency(money.Tax);
        CustomMadeFinalTotalText.Text = FormatCurrency(money.Total);
        CustomMadeResidualText.Text = FormatCurrency(residual);
    }

    private void RefreshAllServicesTotalAmount()
    {
        _totalAmount = _alterationSumTotal + _clothingSumTotal + _customMadeSumTotal;
        TotalAmountText.Text = FormatCurrency(_totalAmount);
        RefreshPaymentSummary();
    }

    private void RefreshPaymentSummary()
    {
        var alterationDown = _alterationMoney.Deposit;
        var customMadeDown = _customMadeMoney.Deposit;
        var clothingDown = _clothingMoney.Deposit;

        // Received deposits: nominal deposits plus tax on any deposit paid by card.
        var receivedDownpayment = _alterationMoney.ReceivedDownpayment + _customMadeMoney.ReceivedDownpayment + _clothingMoney.ReceivedDownpayment;

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
            GetSelectedDownMethod(AlterationDownNone, AlterationDownEtransfer, AlterationDownCard, AlterationDownCash), alterationDown);
        UpdateMethodLabel(AlterationFinalMethodLabel, FinalBalanceMethodKey,
            GetSelectedPaymentMethod(AlterationFinalEtransfer, AlterationFinalCard, AlterationFinalCash), alterationResidual);

        UpdateMethodLabel(CustomMadeDownMethodLabel, DownpaymentMethodKey,
            GetSelectedDownMethod(CustomMadeDownNone, CustomMadeDownEtransfer, CustomMadeDownCard, CustomMadeDownCash), customMadeDown);
        UpdateMethodLabel(CustomMadeFinalMethodLabel, FinalBalanceMethodKey,
            GetSelectedPaymentMethod(CustomMadeFinalEtransfer, CustomMadeFinalCard, CustomMadeFinalCash), customMadeResidual);

        UpdateMethodLabel(ClothingDownMethodLabel, DownpaymentMethodKey,
            GetSelectedDownMethod(ClothingDownNone, ClothingDownEtransfer, ClothingDownCard, ClothingDownCash), clothingDown);
        UpdateMethodLabel(ClothingFinalMethodLabel, FinalBalanceMethodKey,
            GetSelectedPaymentMethod(ClothingFinalEtransfer, ClothingFinalCard, ClothingFinalCash), clothingResidual);
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
            && (StatusReasonCategoryBox.SelectedItem as ComboBoxItem)?.Tag as string == "Other";

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
                item.IsSelected = string.Equals(item.Tag as string, "Other", StringComparison.Ordinal);
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
            ApplyClearAllToSection(clearAll, _alterationSumTotal,
                GetSelectedDownMethod(AlterationDownNone, AlterationDownEtransfer, AlterationDownCard, AlterationDownCash),
                AlterationFinalEtransfer, AlterationFinalCard, AlterationFinalCash, AlterationBalanceClearedCheck);
            ApplyClearAllToSection(clearAll, _customMadeSumTotal,
                GetSelectedDownMethod(CustomMadeDownNone, CustomMadeDownEtransfer, CustomMadeDownCard, CustomMadeDownCash),
                CustomMadeFinalEtransfer, CustomMadeFinalCard, CustomMadeFinalCash, CustomMadeBalanceClearedCheck);
            ApplyClearAllToSection(clearAll, _clothingSumTotal,
                GetSelectedDownMethod(ClothingDownNone, ClothingDownEtransfer, ClothingDownCard, ClothingDownCash),
                ClothingFinalEtransfer, ClothingFinalCard, ClothingFinalCash, ClothingBalanceClearedCheck);
        }
        finally
        {
            _syncingPayment = false;
        }

        UpdatePaymentVisibility();
        RefreshComputedTotals(runAutoComplete: false);
    }

    private static void ApplyClearAllToSection(bool clearAll, decimal sectionTotal, PaymentMethod? downMethod,
        RadioButton finalEtransfer, RadioButton finalCard, RadioButton finalCash, CheckBox balanceClearedCheck)
    {
        if (!clearAll)
        {
            balanceClearedCheck.IsChecked = false;
            return;
        }

        // Nothing is owed on sections without a charge, so leave them untouched.
        if (sectionTotal <= 0m)
            return;

        // Default the final balance to the deposit method ONLY when the user hasn't
        // already picked one. A manually forced final method (e.g. deposit by card,
        // final by cash) must be respected instead of being reset to the deposit way.
        if (GetSelectedPaymentMethod(finalEtransfer, finalCard, finalCash) is null
            && downMethod is not null && downMethod != PaymentMethod.None)
            SetSelectedPaymentMethod(finalEtransfer, finalCard, finalCash, downMethod);

        balanceClearedCheck.IsChecked = true;
    }

    private bool IsOrderBalanceCleared()
    {
        // A brand-new/empty order (no charges anywhere) starts as outstanding.
        if (_totalAmount <= 0m)
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
        var sectionLocked = _isReadOnly || c.BalanceClearedCheck.IsChecked is true;
        // Deposit method radios are frozen once the deposit is marked received OR the whole section is locked.
        var depositMethodLocked = sectionLocked || c.DownCompletedCheck.IsChecked is true;

        c.DownNone.IsEnabled = !depositMethodLocked;
        c.DownEtransfer.IsEnabled = !depositMethodLocked;
        c.DownCard.IsEnabled = !depositMethodLocked;
        c.DownCash.IsEnabled = !depositMethodLocked;
        c.DownCompletedCheck.IsEnabled = !sectionLocked;
        c.FinalEtransfer.IsEnabled = !sectionLocked;
        c.FinalCard.IsEnabled = !sectionLocked;
        c.FinalCash.IsEnabled = !sectionLocked;
        if (sectionLocked)
            c.DownpaymentBox.IsEnabled = false;
    }

    private static void UpdateSectionVisibility(PaymentSectionControls c)
    {
        var anyDownSelected = c.DownNone.IsChecked is true || c.DownEtransfer.IsChecked is true
            || c.DownCard.IsChecked is true || c.DownCash.IsChecked is true;
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

    private decimal? GetTaxRateForServiceType(OrderServiceType serviceType)
        => serviceType switch
        {
            OrderServiceType.Alterations => ParseNullableDecimal(AlterationTaxBox.Text),
            OrderServiceType.ReadyMade => ParseNullableDecimal(ClothingTaxBox.Text),
            _ => null
        };

    private PaymentMethod? GetDownpaymentMethodForServiceType(OrderServiceType serviceType)
        => serviceType switch
        {
            OrderServiceType.Alterations => GetSelectedDownMethod(AlterationDownNone, AlterationDownEtransfer, AlterationDownCard, AlterationDownCash),
            OrderServiceType.ReadyMade => GetSelectedDownMethod(ClothingDownNone, ClothingDownEtransfer, ClothingDownCard, ClothingDownCash),
            OrderServiceType.CustomMade => GetSelectedDownMethod(CustomMadeDownNone, CustomMadeDownEtransfer, CustomMadeDownCard, CustomMadeDownCash),
            _ => null
        };

    private PaymentMethod? GetFinalBalanceMethodForServiceType(OrderServiceType serviceType)
        => serviceType switch
        {
            OrderServiceType.Alterations => GetSelectedPaymentMethod(AlterationFinalEtransfer, AlterationFinalCard, AlterationFinalCash),
            OrderServiceType.ReadyMade => GetSelectedPaymentMethod(ClothingFinalEtransfer, ClothingFinalCard, ClothingFinalCash),
            OrderServiceType.CustomMade => GetSelectedPaymentMethod(CustomMadeFinalEtransfer, CustomMadeFinalCard, CustomMadeFinalCash),
            _ => null
        };

    private void LoadPaymentFields(Order order)
    {
        _syncingPayment = true;
        AlterationDownpaymentBox.Text = order.AlterationDownpayment?.ToString("0.##") ?? string.Empty;
        SetSelectedDownMethod(AlterationDownNone, AlterationDownEtransfer, AlterationDownCard, AlterationDownCash, order.AlterationDownpaymentMethod);
        AlterationDownCompletedCheck.IsChecked = order.AlterationDownpaymentCompleted;
        SetSelectedPaymentMethod(AlterationFinalEtransfer, AlterationFinalCard, AlterationFinalCash, order.AlterationFinalBalanceMethod);
        AlterationBalanceClearedCheck.IsChecked = order.AlterationBalanceCleared;

        CustomMadeDownpaymentBox.Text = order.CustomMadeDownpayment?.ToString("0.##") ?? string.Empty;
        SetSelectedDownMethod(CustomMadeDownNone, CustomMadeDownEtransfer, CustomMadeDownCard, CustomMadeDownCash, order.CustomMadeDownpaymentMethod);
        CustomMadeDownCompletedCheck.IsChecked = order.CustomMadeDownpaymentCompleted;
        SetSelectedPaymentMethod(CustomMadeFinalEtransfer, CustomMadeFinalCard, CustomMadeFinalCash, order.CustomMadeFinalBalanceMethod);
        CustomMadeBalanceClearedCheck.IsChecked = order.CustomMadeBalanceCleared;

        ClothingDownpaymentBox.Text = order.ClothingDownpayment?.ToString("0.##") ?? string.Empty;
        SetSelectedDownMethod(ClothingDownNone, ClothingDownEtransfer, ClothingDownCard, ClothingDownCash, order.ClothingDownpaymentMethod);
        ClothingDownCompletedCheck.IsChecked = order.ClothingDownpaymentCompleted;
        SetSelectedPaymentMethod(ClothingFinalEtransfer, ClothingFinalCard, ClothingFinalCash, order.ClothingFinalBalanceMethod);
        ClothingBalanceClearedCheck.IsChecked = order.ClothingBalanceCleared;

        _syncingPayment = false;
        UpdatePaymentVisibility();
    }

    private void ApplyPaymentFields(Order order)
    {
        // Only persist payment details for sections that actually carry a charge.
        // Unused sections (zero total) keep the UI's default Cash selection, which would
        // otherwise make Order.IsBalanceCleared demand a "balance cleared" flag they never get.
        if (_alterationSumTotal > 0m)
        {
            order.AlterationSubtotal = _alterationSubtotal;
            order.AlterationTaxRate = ParseNullableDecimal(AlterationTaxBox.Text);
            order.AlterationDownpayment = ParseNullableDecimal(AlterationDownpaymentBox.Text);
            order.AlterationDownpaymentMethod = GetSelectedDownMethod(AlterationDownNone, AlterationDownEtransfer, AlterationDownCard, AlterationDownCash);
            order.AlterationDownpaymentCompleted = AlterationDownCompletedCheck.IsChecked.GetValueOrDefault();
            order.AlterationFinalBalanceMethod = GetSelectedPaymentMethod(AlterationFinalEtransfer, AlterationFinalCard, AlterationFinalCash);
            order.AlterationBalanceCleared = AlterationBalanceClearedCheck.IsChecked.GetValueOrDefault();
        }
        else
        {
            order.AlterationSubtotal = null;
            order.AlterationTaxRate = null;
            ClearSectionPaymentFields(
                value => order.AlterationDownpayment = value,
                method => order.AlterationDownpaymentMethod = method,
                completed => order.AlterationDownpaymentCompleted = completed,
                finalMethod => order.AlterationFinalBalanceMethod = finalMethod,
                cleared => order.AlterationBalanceCleared = cleared);
        }

        if (_customMadeSumTotal > 0m)
        {
            order.CustomMadeTaxRate = ParseNullableDecimal(CustomMadeTaxBox.Text);
            order.CustomMadeDownpayment = ParseNullableDecimal(CustomMadeDownpaymentBox.Text);
            order.CustomMadeDownpaymentMethod = GetSelectedDownMethod(CustomMadeDownNone, CustomMadeDownEtransfer, CustomMadeDownCard, CustomMadeDownCash);
            order.CustomMadeDownpaymentCompleted = CustomMadeDownCompletedCheck.IsChecked.GetValueOrDefault();
            order.CustomMadeFinalBalanceMethod = GetSelectedPaymentMethod(CustomMadeFinalEtransfer, CustomMadeFinalCard, CustomMadeFinalCash);
            order.CustomMadeBalanceCleared = CustomMadeBalanceClearedCheck.IsChecked.GetValueOrDefault();
        }
        else
        {
            order.CustomMadeTaxRate = null;
            ClearSectionPaymentFields(
                value => order.CustomMadeDownpayment = value,
                method => order.CustomMadeDownpaymentMethod = method,
                completed => order.CustomMadeDownpaymentCompleted = completed,
                finalMethod => order.CustomMadeFinalBalanceMethod = finalMethod,
                cleared => order.CustomMadeBalanceCleared = cleared);
        }

        if (_clothingSumTotal > 0m)
        {
            order.ClothingSubtotal = _clothingSubtotal;
            order.ClothingTaxRate = ParseNullableDecimal(ClothingTaxBox.Text);
            order.ClothingDownpayment = ParseNullableDecimal(ClothingDownpaymentBox.Text);
            order.ClothingDownpaymentMethod = GetSelectedDownMethod(ClothingDownNone, ClothingDownEtransfer, ClothingDownCard, ClothingDownCash);
            order.ClothingDownpaymentCompleted = ClothingDownCompletedCheck.IsChecked.GetValueOrDefault();
            order.ClothingFinalBalanceMethod = GetSelectedPaymentMethod(ClothingFinalEtransfer, ClothingFinalCard, ClothingFinalCash);
            order.ClothingBalanceCleared = ClothingBalanceClearedCheck.IsChecked.GetValueOrDefault();
        }
        else
        {
            order.ClothingSubtotal = null;
            order.ClothingTaxRate = null;
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

    private static PaymentMethod? GetSelectedDownMethod(RadioButton none, RadioButton etransfer, RadioButton card, RadioButton cash)
    {
        if (none.IsChecked is true)
            return PaymentMethod.None;
        if (etransfer.IsChecked is true)
            return PaymentMethod.Etransfer;
        if (card.IsChecked is true)
            return PaymentMethod.Card;
        if (cash.IsChecked is true)
            return PaymentMethod.Cash;
        return null;
    }

    private static void SetSelectedDownMethod(RadioButton none, RadioButton etransfer, RadioButton card, RadioButton cash, PaymentMethod? method)
    {
        none.IsChecked = method == PaymentMethod.None;
        etransfer.IsChecked = method == PaymentMethod.Etransfer;
        card.IsChecked = method == PaymentMethod.Card;
        cash.IsChecked = method == PaymentMethod.Cash;
    }

    private static PaymentMethod? GetSelectedPaymentMethod(RadioButton etransfer, RadioButton card, RadioButton cash)
    {
        if (etransfer.IsChecked is true)
            return PaymentMethod.Etransfer;
        if (card.IsChecked is true)
            return PaymentMethod.Card;
        if (cash.IsChecked is true)
            return PaymentMethod.Cash;
        return null;
    }

    private static void SetSelectedPaymentMethod(RadioButton etransfer, RadioButton card, RadioButton cash, PaymentMethod? method)
    {
        etransfer.IsChecked = method == PaymentMethod.Etransfer;
        card.IsChecked = method == PaymentMethod.Card;
        cash.IsChecked = method == PaymentMethod.Cash;
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
        foreach (var itemKey in ClothingItemKeys)
        {
            categoryBox.Items.Add(new ComboBoxItem
            {
                Content = _localization[$"ClothingItem.{itemKey}"],
                Tag = itemKey
            });
        }
        categoryBox.SelectedIndex = 0;
        if (!string.IsNullOrWhiteSpace(existingItem?.ProductName))
        {
            foreach (ComboBoxItem comboBoxItem in categoryBox.Items)
            {
                if (string.Equals(comboBoxItem.Tag?.ToString(), existingItem.ProductName, StringComparison.OrdinalIgnoreCase))
                {
                    categoryBox.SelectedItem = comboBoxItem;
                    break;
                }
            }
        }
        Grid.SetColumn(categoryBox, 0);

        var unitPriceBox = new TextBox
        {
            Margin = new Thickness(0, 0, 10, 0),
            Padding = new Thickness(6, 4, 6, 4),
            Text = existingItem?.UnitPrice.ToString("0.##") ?? string.Empty
        };
        RegisterDecimalTextBox(unitPriceBox);
        unitPriceBox.PreviewTextInput += OnDecimalTextBoxPreviewTextInput;
        Grid.SetColumn(unitPriceBox, 1);

        var promotionalPriceBox = new TextBox
        {
            Margin = new Thickness(0, 0, 10, 0),
            Padding = new Thickness(6, 4, 6, 4),
            Text = existingItem?.PromotionalPrice?.ToString("0.##") ?? string.Empty
        };
        RegisterDecimalTextBox(promotionalPriceBox);
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

        unitPriceBox.TextChanged += (_, _) => RefreshClothingTotals();
        promotionalPriceBox.TextChanged += (_, _) => RefreshClothingTotals();
        removeButton.Click += (_, _) =>
        {
            ClothingItemsPanel.Children.Remove(row.Container);
            _clothingItemRows.Remove(row);
            RefreshClothingTotals();
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
            var selectedCategory = (row.CategoryBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? ClothingItemKeys[0];

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
