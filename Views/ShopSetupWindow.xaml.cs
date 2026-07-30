using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
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
/// Local Configuration → Measurement Terms.
///
/// Administrators only. The caller enforces that (the entry points are hidden otherwise); this
/// window trusts its caller for the same reason every other dialog here does.
/// </summary>
public partial class ShopSetupWindow : Window
{
    private readonly LocalizationService _localization;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Shop? _existing;
    private readonly ObservableCollection<LocalizedTextEntry> _names = new();
    private readonly ObservableCollection<LocalizedTextEntry> _addresses = new();
    private readonly ObservableCollection<PaymentTaxRow> _paymentTaxRows = new();
    // The languages-and-currencies selection, held as plain values rather than as live controls
    // because it is now edited in ShopLocalizationWindow. Cancelling that panel leaves these
    // untouched, and Save reads them whichever way they were set.
    private List<string> _installedLanguages = new();
    private string _preferredLanguage = string.Empty;
    private List<CurrencyType> _supportedCurrencies = new();
    private CurrencyType _preferredCurrency;

    // The shop's tax-jurisdiction location. Held as a plain code; the picker seeds the tax matrix
    // from it and Save writes it. Guarded so the initial population does not reseed the matrix a
    // shop was loaded with.
    private string _locationCode = TaxJurisdictions.DefaultCode;
    private bool _locationPopulating;

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
        PopulateAddresses();
        PopulateContact();
        // Languages and currencies are one selection now, edited in ShopLocalizationWindow. This
        // seeds it and writes the summary line the link card shows.
        PopulateLocalization();
        PopulatePaymentTaxRules();
        PopulateLocation();
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
        // One box per installed language rather than a single string: the shop name is printed on
        // receipts and shown in the header, so a zh-CN user should not be reading the English name.
        Fill(_names, _existing?.Names);
        NameItems.ItemsSource = _names;
    }

    /// <summary>
    /// The address editor, which is the name editor's twin — per language for the same reason, and
    /// deliberately optional, so a shop that never fills it in stays valid.
    /// </summary>
    private void PopulateAddresses()
    {
        Fill(_addresses, _existing?.Addresses);
        AddressItems.ItemsSource = _addresses;
    }

    /// <summary>
    /// Phone, email and website. One value each, NOT per language: they are identifiers rather than
    /// prose, so translating them would only invite two versions of one phone number.
    /// </summary>
    private void PopulateContact()
    {
        PhoneField.Load(_existing?.PhoneNumber, _existing);
        EmailBox.Text = _existing?.Email ?? string.Empty;
        WebsiteBox.Text = _existing?.Website ?? string.Empty;
        TaxNumberBox.Text = _existing?.TaxRegistrationNumber ?? string.Empty;
    }

    /// <summary>
    /// Builds one row per installed language, pre-filled from an existing shop where there is one.
    /// The decoded dictionary is passed in already resolved: <see cref="Shop.Names"/> and
    /// <see cref="Shop.Addresses"/> re-parse their JSON on every read, so reading them once beats
    /// reading them per language.
    /// </summary>
    private void Fill(ObservableCollection<LocalizedTextEntry> target, Dictionary<string, string>? existing)
    {
        foreach (var language in _localization.AvailableLanguages)
        {
            target.Add(new LocalizedTextEntry(language.Code, language.Name)
            {
                Value = existing?.GetValueOrDefault(language.Code, string.Empty) ?? string.Empty
            });
        }
    }

    /// <summary>
    /// Opens the languages-and-currencies panel and takes its answer. The form holds the selection
    /// as plain values rather than as live controls, so cancelling the panel changes nothing and
    /// Save reads one set of fields whichever way they were chosen.
    /// </summary>
    private void OnLocalizationClick(object sender, RoutedEventArgs e)
    {
        var panel = new ShopLocalizationWindow(
            _localization, _installedLanguages, _preferredLanguage, _supportedCurrencies, _preferredCurrency)
        {
            Owner = this
        };

        if (panel.ShowDialog() is not true)
            return;

        _installedLanguages = panel.InstalledLanguages;
        _preferredLanguage = panel.PreferredLanguage;
        _supportedCurrencies = panel.SupportedCurrencies;
        _preferredCurrency = panel.PreferredCurrency;
        RefreshLocalizationSummary();
    }

    /// <summary>
    /// The one line the form shows for a decision made elsewhere: the languages, then the currencies.
    /// Named rather than counted ("Chinese, English  ·  CAD, USD") because a count answers "how many"
    /// when the question a manager actually has is "which".
    /// </summary>
    private void RefreshLocalizationSummary()
    {
        var languages = _localization.JoinList(_installedLanguages.Select(code =>
            _localization.AvailableLanguages
                .FirstOrDefault(option => string.Equals(option.Code, code, StringComparison.OrdinalIgnoreCase))
                ?.Name ?? code));

        var currencies = _localization.JoinList(
            _supportedCurrencies.Select(currency => ShopCurrencies.Name(currency, _localization)));

        LocalizationSummaryText.Text = _localization.Format("Shop.Localization.Summary", languages, currencies);
    }

    /// <summary>
    /// The starting selection, before the panel is ever opened.
    /// </summary>
    /// <remarks>
    /// An EXISTING shop is read through ShopLanguages/ShopCurrencies rather than straight off the
    /// row, so a shop configured before these sets existed shows what it has actually been running
    /// in instead of an empty selection that would have to be filled in before anything could save.
    ///
    /// A NEW shop starts with the language the administrator is working in and the currency that
    /// language brings — one of each. Adding a second is a decision about the branch, not a default
    /// to hand out: installing a language its staff cannot read, or advertising money it does not
    /// take, is not a neutral act.
    /// </remarks>
    private void PopulateLocalization()
    {
        if (_existing is null)
        {
            _installedLanguages = new List<string> { _localization.CurrentLanguageCode };
            _preferredLanguage = _localization.CurrentLanguageCode;

            var brought = ShopCurrencies.ForLanguage(_localization.CurrentLanguageCode, _localization);
            _supportedCurrencies = brought.Count > 0
                ? new List<CurrencyType> { brought[0] }
                : new List<CurrencyType> { CurrencySettingService.Instance.Current };
            _preferredCurrency = _supportedCurrencies[0];
        }
        else
        {
            _installedLanguages = ShopLanguages.Installed(_existing, _localization)
                .Select(option => option.Code).ToList();
            _preferredLanguage = ShopLanguages.PreferredCode(_existing, _localization);
            _supportedCurrencies = ShopCurrencies.Supported(_existing, _localization).ToList();
            _preferredCurrency = ShopCurrencies.Preferred(_existing);
        }

        RefreshLocalizationSummary();
    }

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
        FillPaymentTaxRows(rules);
        PaymentTaxItems.ItemsSource = _paymentTaxRows;
    }

    /// <summary>
    /// (Re)builds the matrix rows from a set of rules. Clears first so it can be called again when
    /// the location changes — the ObservableCollection is bound, so the rows on screen follow.
    /// </summary>
    private void FillPaymentTaxRows(PaymentTaxRules rules)
    {
        _paymentTaxRows.Clear();

        foreach (var method in PaymentTaxRules.ConfigurableMethods)
        {
            var rule = rules.For(method);
            _paymentTaxRows.Add(new PaymentTaxRow(
                method,
                _localization[$"PaymentMethod.{method}"],
                AccentColorFor(method),
                rule));
        }
    }

    /// <summary>One option in the location picker: what it reads as, and the code it stores.</summary>
    /// <remarks>
    /// A row type rather than hand-built <c>ComboBoxItem</c>s appended to <c>Items</c>. That shape was
    /// already removed once from this project: an item added before its ComboBox is in a visual tree
    /// logs four binding errors apiece. <c>DisplayMemberPath</c> also needs a property, and
    /// <see cref="TaxJurisdiction.DisplayName"/> is a method — the name has to be resolved here.
    /// </remarks>
    private sealed record LocationRow(string Code, string Name);

    /// <summary>
    /// Fills the store-location picker and points the tax section at the chosen jurisdiction. An
    /// existing shop shows the location it was saved with (an unknown or never-set one resolves to
    /// the home market); a new shop starts on the home market.
    /// </summary>
    /// <remarks>
    /// A NEW shop is seeded from its location's standard rate, because that is the whole promise of
    /// picking a location — it was previously seeded only if the user re-picked one by hand, so a
    /// shop created and saved straight through inherited the generic cash-is-tax-free default instead
    /// of its jurisdiction's. An EXISTING shop is not reseeded: it keeps the exact rules it was saved
    /// with, which may have been tuned by hand.
    /// </remarks>
    private void PopulateLocation()
    {
        _locationPopulating = true;

        _locationCode = TaxJurisdictions.For(_existing).Code;

        LocationBox.DisplayMemberPath = nameof(LocationRow.Name);
        LocationBox.SelectedValuePath = nameof(LocationRow.Code);
        LocationBox.ItemsSource = TaxJurisdictions.All
            .Select(jurisdiction => new LocationRow(jurisdiction.Code, jurisdiction.DisplayName(_localization)))
            .ToList();

        LocationBox.SelectedValue = _locationCode;
        _locationPopulating = false;

        ApplyLocationMode(reseedMatrix: _existing is null);
    }

    /// <summary>
    /// The location changed by hand: adopt it, and reseed the matrix from its standard rate so the
    /// lawful configuration is the starting point. The shop can still edit any method afterwards.
    /// </summary>
    private void OnLocationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_locationPopulating)
            return;

        _locationCode = LocationBox.SelectedValue as string ?? TaxJurisdictions.DefaultCode;
        ApplyLocationMode(reseedMatrix: true);
    }

    /// <summary>
    /// Shows the per-method matrix for an exclusive location and replaces it with the jurisdiction's
    /// single rate for an inclusive one, where a value-added tax cannot vary by how a sale is
    /// settled. Reseeds the matrix from the standard rate when the location was picked by hand.
    /// </summary>
    /// <remarks>
    /// The reseed is NOT limited to exclusive locations any more. It used to be, and the consequence
    /// was that an inclusive location's <c>StandardRatePercent</c> was read nowhere: the rate in force
    /// came from the very matrix this branch hides. The order editor now takes an inclusive rate from
    /// the jurisdiction directly, so reseeding here is hygiene — it keeps the stored matrix from being
    /// stale nonsense if the shop later moves back to a location that uses it — and the rate the shop
    /// is actually taxed at is now stated on screen instead of hidden.
    /// </remarks>
    private void ApplyLocationMode(bool reseedMatrix)
    {
        var jurisdiction = TaxJurisdictions.Find(_locationCode) ?? TaxJurisdictions.Default;
        var inclusive = jurisdiction.PricesIncludeTax;

        TaxMatrixPanel.Visibility = inclusive ? Visibility.Collapsed : Visibility.Visible;
        // The section hint describes the matrix, so it goes with it rather than staying to contradict
        // the inclusive note underneath.
        TaxSectionHint.Visibility = inclusive ? Visibility.Collapsed : Visibility.Visible;
        TaxInclusivePanel.Visibility = inclusive ? Visibility.Visible : Visibility.Collapsed;
        TaxInclusiveRateText.Text = _localization.Format("Shop.Tax.InclusiveRate",
            jurisdiction.StandardRatePercent.ToString("0.##", CultureInfo.CurrentCulture));

        // The tax number is a different question from the rate: ask for it only where the location
        // issues one, and call it what that location calls it. A hidden TextBox keeps its text in WPF,
        // so a number already stored by a shop that relocates is never quietly wiped on save.
        TaxNumberLabel.Text = jurisdiction.TaxNumberName(_localization);
        TaxNumberPanel.Visibility = jurisdiction.CollectsTaxNumber ? Visibility.Visible : Visibility.Collapsed;

        // The shop's own phone follows the location it is being given — but only while the box is
        // empty, so re-picking a location never re-codes a number somebody typed.
        PhoneField.FollowLocation(_locationCode);

        if (reseedMatrix && ConfirmReseed(jurisdiction))
            FillPaymentTaxRows(PaymentTaxRules.CreateForStandardRate(jurisdiction.StandardRatePercent));
    }

    /// <summary>
    /// Whether reseeding from this jurisdiction would throw away rules somebody configured — the
    /// question, kept apart from the asking.
    /// </summary>
    /// <remarks>
    /// The split is not decoration. A <c>MessageBox</c> reached from inside a
    /// <c>SelectionChanged</c> handler blocks the thread that raised it, so any harness that drives
    /// the picker hangs on a dialog nothing can answer — which is exactly what happened, and it looks
    /// like a slow test rather than a stuck one. A pure predicate can be asserted directly, and the
    /// prompt stays where a person is present to answer it.
    ///
    /// False for a NEW shop: there is nothing to lose, and the seed is the point of picking a
    /// location. False when the rows already match the seed, so the prompt only ever appears when it
    /// is genuinely about to discard something — which includes the sharpest case, a location with no
    /// single rate to quote (Canada and the US, both at 0%) zeroing a whole configured matrix. That
    /// case is now the ORDINARY one rather than the exotic one: no tax-exclusive location quotes a
    /// rate any more, so any shop that has typed in the rate it collects loses it the moment it picks
    /// a different location, and this prompt is the only thing standing in front of that.
    /// </remarks>
    private bool WouldDiscardConfiguredRules(TaxJurisdiction jurisdiction)
    {
        if (_existing is null)
            return false;

        var seed = PaymentTaxRules.CreateForStandardRate(jurisdiction.StandardRatePercent);
        return !_paymentTaxRows.All(row =>
        {
            var seeded = seed.For(row.Method);
            return row.IsTaxable == seeded.IsTaxable && row.TryResolveRate(out var rate) && rate == seeded.RatePercent;
        });
    }

    /// <summary>Asks before overwriting a matrix somebody configured; silent when nothing is at stake.</summary>
    private bool ConfirmReseed(TaxJurisdiction jurisdiction)
        => !WouldDiscardConfiguredRules(jurisdiction)
           || MessageBox.Show(
                  _localization.Format("Shop.Tax.ReseedConfirm", jurisdiction.DisplayName(_localization)),
                  _localization["Shop.Tax.ReseedConfirmTitle"],
                  MessageBoxButton.YesNo,
                  MessageBoxImage.Question) == MessageBoxResult.Yes;

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
        var names = CollectFilled(_names);

        // At least one language, not every language: Shop.ResolveName already falls back to any
        // language that has a name, so requiring all of them would be busywork.
        if (names.Count == 0)
        {
            ShowError("Shop.Setup.NameRequired");
            return;
        }

        // No such check for the address: it is optional, so an empty dictionary is a valid answer
        // and means "this shop has not given one".
        var addresses = CollectFilled(_addresses);

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

        // A shop with no language is a shop nobody can read; a shop with no currency cannot price an
        // order at all. The localization panel refuses to return either, and a NEW shop is seeded
        // with one of each, so these are belt-and-braces — but they are the last gate before a write,
        // and the panel is not the only thing that could ever set these fields.
        if (_installedLanguages.Count == 0)
        {
            ShowError("Shop.Setup.InstalledLanguagesRequired");
            return;
        }

        if (_supportedCurrencies.Count == 0)
        {
            ShowError("Shop.Setup.SupportedCurrenciesRequired");
            return;
        }

        // The panel only ever returns a preference drawn from what was selected, so these already
        // agree. The fallbacks cover a shop seeded before either set existed rather than saving one
        // that opens in a language it does not run in, or prices in money it does not take.
        var languageCode = _installedLanguages.Contains(_preferredLanguage, StringComparer.OrdinalIgnoreCase)
            ? _preferredLanguage
            : _installedLanguages[0];
        var currencyType = _supportedCurrencies.Contains(_preferredCurrency)
            ? _preferredCurrency
            : _supportedCurrencies[0];

        var settings = new ShopFormValues(
            names, addresses, languageCode, _installedLanguages, currencyType, _supportedCurrencies, taxRules, _locationCode);

        Shop = _existing is null ? CreateShop(settings) : UpdateShop(settings);

        DialogResult = true;
    }

    /// <summary>
    /// Keeps only the languages that were actually filled in, trimmed. A blank box means "no value
    /// for this language", not "an empty value" — storing the empty string would defeat
    /// <see cref="Shop.ResolveName"/>'s fallback, which skips whitespace entries anyway.
    /// </summary>
    private static Dictionary<string, string> CollectFilled(IEnumerable<LocalizedTextEntry> entries)
        => entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Value))
            .ToDictionary(entry => entry.LanguageCode, entry => entry.Value.Trim());

    /// <summary>
    /// Copies the contact boxes onto the shop, blank meaning null rather than "". Same shape as
    /// <see cref="ApplyReceiptFormat"/>: it reads the controls directly instead of adding three
    /// more parameters to the two save methods.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static",
        Justification = "False positive: PhoneField, EmailBox and WebsiteBox are x:Name instance fields " +
                        "from the XAML-generated partial, which the analyzer does not see. The method " +
                        "reads instance data and cannot be static.")]
    private void ApplyContactDetails(Shop shop)
    {
        shop.PhoneNumber = Blank(PhoneField.FullNumber);
        shop.Email = Blank(EmailBox.Text);
        shop.Website = Blank(WebsiteBox.Text);
        shop.TaxRegistrationNumber = Blank(TaxNumberBox.Text);

        static string? Blank(string value)
        {
            var trimmed = value.Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }
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

    /// <summary>
    /// Everything the form collects about a shop, so the two save paths take one argument each and
    /// cannot disagree about what the form said.
    /// </summary>
    private sealed record ShopFormValues(
        Dictionary<string, string> Names,
        Dictionary<string, string> Addresses,
        string LanguageCode,
        List<string> InstalledLanguages,
        CurrencyType CurrencyType,
        List<CurrencyType> SupportedCurrencies,
        PaymentTaxRules TaxRules,
        string LocationCode);

    private Shop CreateShop(ShopFormValues values)
    {
        var shop = new Shop
        {
            PublicId = Guid.NewGuid(),
            PreferredLanguageCode = values.LanguageCode,
            CurrencyType = values.CurrencyType,
            LocationCode = values.LocationCode,
            CreatedAtUtc = DateTime.UtcNow
        };
        shop.SetNames(values.Names);
        shop.SetAddresses(values.Addresses);
        shop.SetInstalledLanguages(values.InstalledLanguages);
        shop.SetSupportedCurrencies(values.SupportedCurrencies);
        shop.SetPaymentTaxRules(values.TaxRules);
        ApplyContactDetails(shop);
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
        {
            MeasurementTermsService.CopyConfigBetweenShops(source, shop);
            ProductCatalogService.CopyConfigBetweenShops(source, shop);
        }

        ConfigureTermsRequested = ConfigureNowCheck.IsChecked is true;

        return shop;
    }

    private Shop UpdateShop(ShopFormValues values)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Read detached and Update, rather than mutating a tracked entity: the instance is handed
        // back to the caller, which makes it the active shop long after this scope is disposed.
        // Every other shop read in the app follows the same AsNoTracking shape.
        var shop = db.Shops.AsNoTracking().First(candidate => candidate.Id == _existing!.Id);
        shop.SetNames(values.Names);
        shop.SetAddresses(values.Addresses);
        shop.PreferredLanguageCode = values.LanguageCode;
        shop.SetInstalledLanguages(values.InstalledLanguages);
        shop.CurrencyType = values.CurrencyType;
        shop.LocationCode = values.LocationCode;
        shop.SetSupportedCurrencies(values.SupportedCurrencies);
        shop.SetPaymentTaxRules(values.TaxRules);
        ApplyContactDetails(shop);
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

    /// <summary>
    /// One language's value of a per-language text field, as the name and address editors bind to
    /// it. Shared by both rather than duplicated — they are the same row with a different label.
    /// </summary>
    private sealed class LocalizedTextEntry : INotifyPropertyChanged
    {
        private string _value = string.Empty;

        public LocalizedTextEntry(string languageCode, string languageName)
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
