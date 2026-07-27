using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using CameywareOrder.Data;
using CameywareOrder.Localization;
using CameywareOrder.Models;
using CameywareOrder.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CameywareOrder.Views;

/// <summary>
/// Creates a shop, and edits an existing one. Both modes write the same fields — name per language,
/// preferred language and currency — so they share a window; only the measurement-terms seeding is
/// creation-only, because a shop that already exists has terms of its own to edit through
/// 本地配置 → 量身项目设置.
///
/// Administrators only. The caller enforces that (the entry points are hidden otherwise); this
/// window trusts its caller for the same reason every other dialog here does.
/// </summary>
public partial class ShopSetupWindow : Window
{
    private readonly LocalizationService _localization;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Shop? _existing;
    private readonly ObservableCollection<ShopNameEntry> _names = new();
    private readonly ObservableCollection<PaymentTaxRow> _paymentTaxRows = new();

    /// <param name="existing">The shop to edit, or null to create a new one.</param>
    public ShopSetupWindow(
        LocalizationService localization,
        IServiceScopeFactory scopeFactory,
        Shop? existing = null)
    {
        InitializeComponent();

        _localization = localization;
        _scopeFactory = scopeFactory;
        _existing = existing;

        var isEdit = existing is not null;

        Title = _localization[isEdit ? "Shop.Setup.EditTitle" : "Shop.Setup.CreateTitle"];
        TitleText.Text = Title;

        // An existing shop's terms are edited through the normal terms editor; offering to reseed
        // them here would be a one-click way to discard a branch's configuration.
        TermsSection.Visibility = isEdit ? Visibility.Collapsed : Visibility.Visible;

        PopulateNames();
        PopulateLanguages();
        PopulateCurrencies();
        PopulatePaymentTaxRules();
        PopulateReceiptFormat();

        if (!isEdit)
            PopulateCopySources();
    }

    /// <summary>The created or updated shop, or null when the window was cancelled.</summary>
    public Shop? Shop { get; private set; }

    /// <summary>Whether the user asked to configure the new shop's measurement terms straight away.</summary>
    public bool ConfigureTermsRequested { get; private set; }

    // --- Population -------------------------------------------------------------

    private void PopulateNames()
    {
        // Names decodes NamesJson on every read, so it is resolved once here rather than per language.
        var existingNames = _existing?.Names ?? new Dictionary<string, string>();

        // One box per installed language rather than a single string: the shop name is printed on
        // receipts and shown in the header, so a zh-CN user should not be reading the English name.
        foreach (var language in _localization.AvailableLanguages)
        {
            _names.Add(new ShopNameEntry(language.Code, language.Name)
            {
                Value = existingNames.GetValueOrDefault(language.Code, string.Empty)
            });
        }

        NameItems.ItemsSource = _names;
    }

    private void PopulateLanguages()
    {
        LanguageBox.ItemsSource = _localization.AvailableLanguages;
        LanguageBox.DisplayMemberPath = nameof(LanguageOption.Name);
        LanguageBox.SelectedValuePath = nameof(LanguageOption.Code);

        // A new shop defaults to the language the administrator is currently working in, which is
        // nearly always the one they want.
        LanguageBox.SelectedValue = _existing?.PreferredLanguageCode ?? _localization.CurrentLanguageCode;

        if (LanguageBox.SelectedValue is null)
            LanguageBox.SelectedIndex = 0;
    }

    private void PopulateCurrencies()
    {
        CurrencyBox.SelectedValuePath = nameof(ComboBoxItem.Tag);
        CurrencyBox.Items.Add(CreateCurrencyItem(CurrencyType.CAD));
        CurrencyBox.Items.Add(CreateCurrencyItem(CurrencyType.USD));
        CurrencyBox.Items.Add(CreateCurrencyItem(CurrencyType.CNY));

        CurrencyBox.SelectedValue = _existing?.CurrencyType ?? CurrencyType.CAD;
    }

    private ComboBoxItem CreateCurrencyItem(CurrencyType currencyType)
        => new()
        {
            Content = _localization[$"CurrencyType.{currencyType}"],
            Tag = currencyType
        };

    private void PopulateCopySources()
    {
        CopySourceBox.SelectedValuePath = nameof(ComboBoxItem.Tag);

        foreach (var shop in LoadShops())
        {
            CopySourceBox.Items.Add(new ComboBoxItem
            {
                Content = shop.ResolveName(_localization.CurrentLanguageCode),
                Tag = shop
            });
        }

        CopySourceBox.SelectedIndex = CopySourceBox.Items.Count > 0 ? 0 : -1;

        // Nothing to copy from on a first-ever shop, so do not offer the option at all.
        CopyFromRadio.IsEnabled = CopySourceBox.Items.Count > 0;
    }

    private List<Shop> LoadShops()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return db.Shops
            .AsNoTracking()
            .Where(shop => !shop.IsArchived)
            .OrderBy(shop => shop.Id)
            .ToList();
    }

    /// <summary>
    /// One row per configurable payment method. Generated from
    /// <see cref="PaymentTaxRules.ConfigurableMethods"/> rather than written out in XAML, so a
    /// method added to the enum later shows up here with no view change.
    /// </summary>
    private void PopulatePaymentTaxRules()
    {
        var rules = _existing?.PaymentTaxRules ?? PaymentTaxRules.CreateDefault();

        foreach (var method in PaymentTaxRules.ConfigurableMethods)
        {
            var rule = rules.For(method);
            _paymentTaxRows.Add(new PaymentTaxRow(
                method,
                _localization[$"PaymentMethod.{method}"],
                AccentColorFor(method),
                rule));
        }

        PaymentTaxItems.ItemsSource = _paymentTaxRows;
    }

    // Cash green, cards blue/purple, e-transfer teal — the colour only has to make the four rows
    // scannable at a glance, so it is fixed per method rather than configurable.
    private static string AccentColorFor(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "#27AE60",
        PaymentMethod.DebitCard => "#2980B9",
        PaymentMethod.CreditCard => "#8E44AD",
        _ => "#16A085"
    };

    private void PopulateReceiptFormat()
    {
        var mode = _existing?.OrderNumberMode ?? OrderNumberMode.Timestamp;

        PrefixBox.Text = _existing is null
            ? OrderNumberFormatter.DefaultPrefix
            : _existing.OrderNumberPrefix ?? OrderNumberFormatter.DefaultPrefix;

        foreach (var padding in new[] { 3, 4, 5, 6, 8 })
            PaddingBox.Items.Add(padding);
        PaddingBox.SelectedItem = OrderNumberFormatter.ResolvePadding(_existing ?? new Shop());

        NextNumberBox.Text = (_existing?.OrderNumberNextSequence ?? 1).ToString();

        TimestampModeRadio.IsChecked = mode == OrderNumberMode.Timestamp;
        SequentialModeRadio.IsChecked = mode == OrderNumberMode.Sequential;
        DailyModeRadio.IsChecked = mode == OrderNumberMode.DailySequential;
        YearlyModeRadio.IsChecked = mode == OrderNumberMode.YearlySequential;

        RefreshReceiptPreview();
    }

    private void OnReceiptFormatChanged(object sender, TextChangedEventArgs e) => RefreshReceiptPreview();

    private void OnReceiptFormatSelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshReceiptPreview();

    private void OnReceiptModeChanged(object sender, RoutedEventArgs e)
    {
        // Checked fires while InitializeComponent runs, before the other controls exist.
        if (PrefixBox is null)
            return;

        // A timestamp is unique by the second, so it keeps no counter and pads nothing.
        var usesCounter = SelectedOrderNumberMode() != OrderNumberMode.Timestamp;
        PaddingBox.IsEnabled = usesCounter;
        NextNumberBox.IsEnabled = usesCounter;

        RefreshReceiptPreview();
    }

    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static",
        Justification = "Reads the x:Name radio buttons SequentialModeRadio/DailyModeRadio/YearlyModeRadio, " +
                        "which live in the XAML-generated partial that standalone analysis cannot see.")]
    private OrderNumberMode SelectedOrderNumberMode()
    {
        if (SequentialModeRadio.IsChecked.GetValueOrDefault())
            return OrderNumberMode.Sequential;
        if (DailyModeRadio.IsChecked.GetValueOrDefault())
            return OrderNumberMode.DailySequential;
        if (YearlyModeRadio.IsChecked.GetValueOrDefault())
            return OrderNumberMode.YearlySequential;
        return OrderNumberMode.Timestamp;
    }

    /// <summary>
    /// Shows the number the next order would actually get. Built through the same formatter the
    /// order editor uses, so the preview cannot drift from the real thing.
    /// </summary>
    private void RefreshReceiptPreview()
    {
        var preview = new Shop
        {
            OrderNumberMode = SelectedOrderNumberMode(),
            OrderNumberPrefix = PrefixBox.Text,
            OrderNumberPadding = PaddingBox.SelectedItem as int? ?? 4,
            OrderNumberNextSequence = int.TryParse(NextNumberBox.Text, out var next) ? next : 1
        };
        preview.OrderNumberSequenceKey =
            OrderNumberFormatter.SequenceKeyFor(preview.OrderNumberMode, DateTime.Now);

        ReceiptPreviewText.Text = OrderNumberFormatter.Preview(preview, DateTime.Now);
    }

    private void OnTermsSourceChanged(object sender, RoutedEventArgs e)
    {
        // Guarded: Checked fires during InitializeComponent, before CopySourceBox exists.
        if (CopySourceBox is not null)
            CopySourceBox.IsEnabled = CopyFromRadio.IsChecked is true;
    }

    // --- Save -------------------------------------------------------------------

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var names = _names
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Value))
            .ToDictionary(entry => entry.LanguageCode, entry => entry.Value.Trim());

        // At least one language, not every language: Shop.ResolveName already falls back to any
        // language that has a name, so requiring all of them would be busywork.
        if (names.Count == 0)
        {
            ShowError("Shop.Setup.NameRequired");
            return;
        }

        if (IsNameTaken(names))
        {
            ShowError("Shop.Setup.NameDuplicate");
            return;
        }

        if (!TryBuildPaymentTaxRules(out var taxRules))
        {
            ShowError("Shop.Tax.RateInvalid");
            return;
        }

        var languageCode = LanguageBox.SelectedValue as string;
        var currencyType = CurrencyBox.SelectedValue as CurrencyType? ?? CurrencyType.CAD;

        Shop = _existing is null
            ? CreateShop(names, languageCode, currencyType, taxRules)
            : UpdateShop(names, languageCode, currencyType, taxRules);

        DialogResult = true;
    }

    /// <summary>
    /// Reads the payment/tax matrix back, rejecting a rate that is not a percentage. Returns false
    /// without touching the shop, so a typo cannot half-apply.
    /// </summary>
    private bool TryBuildPaymentTaxRules(out PaymentTaxRules rules)
    {
        rules = new PaymentTaxRules();

        foreach (var row in _paymentTaxRows)
        {
            if (!row.TryResolveRate(out var rate))
                return false;

            rules.Methods[row.Method.ToString()] = new PaymentTaxRule
            {
                IsTaxable = row.IsTaxable,
                RatePercent = rate
            };
        }

        return true;
    }

    /// <summary>
    /// Copies the receipt-numbering choices onto the shop. The sequence key is stamped with the
    /// current period so a hand-set "next number" is not immediately reset by the daily/yearly
    /// rollover check the moment it is saved.
    /// </summary>
    private void ApplyReceiptFormat(Shop shop)
    {
        var mode = SelectedOrderNumberMode();
        var prefix = PrefixBox.Text.Trim();

        shop.OrderNumberMode = mode;
        shop.OrderNumberPrefix = string.IsNullOrWhiteSpace(prefix) ? null : prefix;
        shop.OrderNumberPadding = PaddingBox.SelectedItem as int? ?? 4;
        shop.OrderNumberNextSequence = int.TryParse(NextNumberBox.Text, out var next) && next > 0 ? next : 1;
        shop.OrderNumberSequenceKey = OrderNumberFormatter.SequenceKeyFor(mode, DateTime.Now);
    }

    private Shop CreateShop(
        Dictionary<string, string> names, string? languageCode, CurrencyType currencyType, PaymentTaxRules taxRules)
    {
        var shop = new Shop
        {
            PublicId = Guid.NewGuid(),
            PreferredLanguageCode = languageCode,
            CurrencyType = currencyType,
            CreatedAtUtc = DateTime.UtcNow
        };
        shop.SetNames(names);
        shop.SetPaymentTaxRules(taxRules);
        ApplyReceiptFormat(shop);

        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Shops.Add(shop);
            db.SaveChanges();
        }

        // AFTER SaveChanges, never before: the terms file is keyed on PublicId, which is assigned
        // above, but copying before the row exists would leave an orphaned file behind if the
        // insert failed.
        if (CopyFromRadio.IsChecked is true && CopySourceBox.SelectedValue is Shop source)
            MeasurementTermsService.CopyConfigBetweenShops(source, shop);

        ConfigureTermsRequested = ConfigureNowCheck.IsChecked is true;

        return shop;
    }

    private Shop UpdateShop(
        Dictionary<string, string> names, string? languageCode, CurrencyType currencyType, PaymentTaxRules taxRules)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Read detached and Update, rather than mutating a tracked entity: the instance is handed
        // back to the caller, which makes it the active shop long after this scope is disposed.
        // Every other shop read in the app follows the same AsNoTracking shape.
        var shop = db.Shops.AsNoTracking().First(candidate => candidate.Id == _existing!.Id);
        shop.SetNames(names);
        shop.PreferredLanguageCode = languageCode;
        shop.CurrencyType = currencyType;
        shop.SetPaymentTaxRules(taxRules);
        ApplyReceiptFormat(shop);

        db.Shops.Update(shop);
        db.SaveChanges();

        return shop;
    }

    /// <summary>
    /// Rejects a name already used by another shop, in any language. Two branches that read
    /// identically in the picker are indistinguishable to whoever has to choose between them.
    /// </summary>
    private bool IsNameTaken(Dictionary<string, string> names)
    {
        var wanted = names.Values
            .Select(value => value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return LoadShops()
            .Where(shop => shop.Id != _existing?.Id)
            .SelectMany(shop => shop.Names.Values)
            .Any(existing => !string.IsNullOrWhiteSpace(existing) && wanted.Contains(existing.Trim()));
    }

    private void ShowError(string key)
    {
        ErrorText.Text = _localization[key];
        ErrorText.Visibility = Visibility.Visible;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>One language's shop name, as the name editor binds to it.</summary>
    private sealed class ShopNameEntry : INotifyPropertyChanged
    {
        private string _value = string.Empty;

        public ShopNameEntry(string languageCode, string languageName)
        {
            LanguageCode = languageCode;
            LanguageName = languageName;
        }

        public string LanguageCode { get; }

        public string LanguageName { get; }

        public string Value
        {
            get => _value;
            set
            {
                _value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>
    /// One payment method's row in the tax matrix. Tax free and taxable are two radios in one
    /// group, so setting either drives the other — WPF unchecks the sibling but does not push that
    /// back through a one-way-to-source binding, and the rate box's enabled state hangs off it.
    /// </summary>
    private sealed class PaymentTaxRow : INotifyPropertyChanged
    {
        private bool _isTaxable;
        private string _rateText;

        public PaymentTaxRow(PaymentMethod method, string displayName, string accentColor, PaymentTaxRule rule)
        {
            Method = method;
            DisplayName = displayName;
            AccentColor = accentColor;
            _isTaxable = rule.IsTaxable;
            _rateText = rule.RatePercent.ToString("0.##");
        }

        public PaymentMethod Method { get; }

        public string DisplayName { get; }

        public string AccentColor { get; }

        /// <summary>Each row's two radios need their own group, or all eight would be one choice.</summary>
        [SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed",
            Justification = "Bound as GroupName=\"{Binding GroupName}\" in the PaymentTaxItems item template " +
                            "in ShopSetupWindow.xaml; XAML bindings are invisible to Roslyn. Deleting it would " +
                            "merge every row's radios into one group.")]
        public string GroupName => $"TaxMode_{Method}";

        public bool IsTaxable
        {
            get => _isTaxable;
            set
            {
                if (_isTaxable == value)
                    return;

                _isTaxable = value;
                Notify(nameof(IsTaxable));
                Notify(nameof(IsTaxFree));
                Notify(nameof(StateSummary));
            }
        }

        public bool IsTaxFree
        {
            get => !_isTaxable;
            set => IsTaxable = !value;
        }

        public string RateText
        {
            get => _rateText;
            set
            {
                _rateText = value;
                Notify(nameof(RateText));
                Notify(nameof(StateSummary));
            }
        }

        /// <summary>Plain-language echo of the row, so the setting reads without decoding the controls.</summary>
        public string StateSummary
        {
            get
            {
                var localization = LocalizationService.Instance;

                if (!IsTaxable)
                    return localization["Shop.Tax.SummaryFree"];

                return TryResolveRate(out var rate)
                    ? localization.Format("Shop.Tax.SummaryTaxable", rate.ToString("0.##"))
                    : localization["Shop.Tax.RateInvalid"];
            }
        }

        /// <summary>
        /// The row's rate as a percentage, or false when what was typed is not one. A tax-free row
        /// always resolves — whatever is in its (disabled) box is irrelevant.
        /// </summary>
        public bool TryResolveRate(out decimal rate)
        {
            rate = 0m;

            if (!IsTaxable)
                return true;

            if (!decimal.TryParse(_rateText, out var parsed) || parsed < 0m || parsed > 100m)
                return false;

            rate = parsed;
            return true;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Notify(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
