using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using CameywareOrder.Localization;
using CameywareOrder.Models;
using CameywareOrder.Services;

namespace CameywareOrder.Views;

/// <summary>
/// Chooses the languages a shop runs in and the currencies it takes — one panel, because they are
/// one decision. A language brings the currencies of its market, so the right-hand side is a
/// consequence of the left.
/// </summary>
/// <remarks>
/// Split out of <see cref="ShopSetupWindow"/>, which had grown two tick lists and two pickers inside
/// an already long form. It edits nothing directly: the caller passes the current selection in and
/// reads the result off the properties, so cancelling costs nothing and the shop is written in one
/// place.
/// </remarks>
public partial class ShopLocalizationWindow : Window
{
    private readonly LocalizationService _localization;
    private readonly ObservableCollection<LanguageRow> _languages = new();
    private readonly ObservableCollection<CurrencyGroup> _groups = new();

    /// <summary>
    /// One row per currency, keyed by currency — NOT one per (language, currency) pair. EUR is
    /// brought by both Français and Español, and two independent tick boxes for it would let the
    /// panel contradict itself: ticked under one language, clear under the other, with no answer to
    /// "does this shop take euros". Sharing the instance makes the two cards two VIEWS of one fact.
    /// </summary>
    private readonly Dictionary<CurrencyType, CurrencyRow> _currencyRows = new();

    /// <summary>Set while the panel re-ticks a currency itself, so the repair does not re-enter.</summary>
    private bool _repairing;

    public ShopLocalizationWindow(
        LocalizationService localization,
        IReadOnlyList<string> installedLanguages,
        string preferredLanguage,
        IReadOnlyList<CurrencyType> supportedCurrencies,
        CurrencyType preferredCurrency)
    {
        InitializeComponent();
        _localization = localization;

        BuildLanguages(installedLanguages);
        BuildCurrencyRows(supportedCurrencies);
        RefreshCurrencyGroups();

        LanguageItems.ItemsSource = _languages;
        CurrencyGroupItems.ItemsSource = _groups;

        RefreshLanguageBox(preferredLanguage);
        // Run the guard on the way in, not only on the way out: a shop can arrive holding a currency
        // no ticked language brings, and the panel would otherwise open on a selection it does not
        // show. See EnsureOneCurrency.
        EnsureOneCurrency();
        RefreshCurrencyBox(preferredCurrency);
    }

    /// <summary>The languages ticked, in shipped order. Only meaningful once the dialog returns true.</summary>
    public List<string> InstalledLanguages { get; private set; } = new();

    public string PreferredLanguage { get; private set; } = string.Empty;

    /// <summary>The currencies ticked, ordered by the offer rather than by when they were clicked.</summary>
    public List<CurrencyType> SupportedCurrencies { get; private set; } = new();

    public CurrencyType PreferredCurrency { get; private set; }

    // ── building ──────────────────────────────────────────────────────────────────────────────

    private void BuildLanguages(IReadOnlyList<string> installed)
    {
        var wanted = installed.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var language in _localization.AvailableLanguages)
        {
            var currencies = ShopCurrencies.ForLanguage(language.Code, _localization);
            var row = new LanguageRow(
                language.Code,
                language.Name,
                _localization.JoinList(currencies.Select(currency => currency.ToString())))
            {
                IsInstalled = wanted.Contains(language.Code)
            };

            row.PropertyChanged += OnLanguageToggled;
            _languages.Add(row);
        }
    }

    /// <summary>
    /// One shared row per currency the installation offers, plus any the shop already accepts that
    /// the offer no longer contains — a language may have been uninstalled since, and a branch must
    /// not silently stop taking money it had said it takes.
    /// </summary>
    private void BuildCurrencyRows(IReadOnlyList<CurrencyType> supported)
    {
        var offered = ShopCurrencies.Offered(_localization);

        foreach (var currency in offered.Concat(supported.Where(currency => !offered.Contains(currency))))
        {
            if (_currencyRows.ContainsKey(currency))
                continue;

            var row = new CurrencyRow(
                currency,
                ShopCurrencies.Name(currency, _localization),
                CurrencySettingService.GetSymbol(currency))
            {
                IsSupported = supported.Contains(currency)
            };

            row.PropertyChanged += OnCurrencyToggled;
            _currencyRows[currency] = row;
        }
    }

    private void OnLanguageToggled(object? sender, PropertyChangedEventArgs e)
    {
        RefreshCurrencyGroups();
        RefreshLanguageBox(LanguageBox.SelectedValue as string);
        EnsureOneCurrency();
        RefreshCurrencyBox(CurrencyBox.SelectedValue as CurrencyType?);
    }

    private void OnCurrencyToggled(object? sender, PropertyChangedEventArgs e)
    {
        // EnsureOneCurrency ticks a row when the last one is cleared, which raises this handler again.
        if (_repairing)
            return;

        EnsureOneCurrency();
        RefreshCurrencyBox(CurrencyBox.SelectedValue as CurrencyType?);
    }

    /// <summary>
    /// Rebuilds the right pane from the ticked languages. A language that brings no currency still
    /// gets a card, carrying a note saying so — silently omitting it would read as the panel having
    /// missed the language.
    /// </summary>
    private void RefreshCurrencyGroups()
    {
        _groups.Clear();

        foreach (var language in _languages.Where(row => row.IsInstalled))
        {
            var currencies = ShopCurrencies.ForLanguage(language.Code, _localization)
                .Where(_currencyRows.ContainsKey)
                .Select(currency => _currencyRows[currency])
                .ToList();

            _groups.Add(new CurrencyGroup(language.Name, currencies));
        }

        CurrenciesEmptyNotice.Visibility = _groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Offers only the ticked languages: a shop must open in one it actually runs in.</summary>
    private void RefreshLanguageBox(string? preferred)
    {
        LanguageBox.ItemsSource = _localization.AvailableLanguages
            .Where(option => _languages.Any(row =>
                row.IsInstalled && string.Equals(row.Code, option.Code, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        LanguageBox.DisplayMemberPath = nameof(LanguageOption.Name);
        LanguageBox.SelectedValuePath = nameof(LanguageOption.Code);
        LanguageBox.SelectedValue = preferred;

        if (LanguageBox.SelectedValue is null && LanguageBox.Items.Count > 0)
            LanguageBox.SelectedIndex = 0;
    }

    /// <summary>
    /// Offers only the ticked currencies, and in the OFFER's order — English's first, CAD before USD
    /// — so every shop presents them the same way regardless of the order they were clicked in.
    /// </summary>
    /// <remarks>
    /// ItemsSource over the shared rows, NOT hand-built <see cref="ComboBoxItem"/>s added to Items.
    /// A ComboBoxItem constructed before its ComboBox is in a visual tree has no ItemsControl to
    /// resolve the stock template's `RelativeSource FindAncestor` alignment bindings against, and
    /// WPF logs four binding errors per picker for it. Letting the ComboBox generate its own
    /// containers avoids that, and makes the picker a view over the same rows the right pane ticks.
    /// </remarks>
    private void RefreshCurrencyBox(CurrencyType? preferred)
    {
        CurrencyBox.ItemsSource = TickedCurrencies()
            .Select(currency => _currencyRows[currency])
            .ToList();
        CurrencyBox.DisplayMemberPath = nameof(CurrencyRow.Name);
        CurrencyBox.SelectedValuePath = nameof(CurrencyRow.Currency);

        CurrencyBox.SelectedValue = preferred;
        if (CurrencyBox.SelectedValue is null && CurrencyBox.Items.Count > 0)
            CurrencyBox.SelectedIndex = 0;
    }

    /// <summary>
    /// Every currency the right pane currently shows a tick box for: what the TICKED languages bring,
    /// in the offer's order — English's first, CAD before USD.
    /// </summary>
    private List<CurrencyType> OfferedByTickedLanguages()
    {
        var brought = _languages
            .Where(row => row.IsInstalled)
            .SelectMany(row => ShopCurrencies.ForLanguage(row.Code, _localization))
            .ToHashSet();

        return ShopCurrencies.Offered(_localization).Where(brought.Contains).ToList();
    }

    /// <summary>The currencies this shop takes: ticked, and shown on this screen.</summary>
    /// <remarks>
    /// Scoped to <see cref="OfferedByTickedLanguages"/> — the rows the right pane displays — because a
    /// row can be ticked and invisible. The rows are seeded from every language installed on the
    /// SYSTEM plus whatever the shop already accepted, while the cards are grouped by the languages
    /// this SHOP runs in. Those two sets are not the same: a shop holding CAD and JPY with only
    /// English and Français ticked had JPY in this record and in the preferred-currency picker, with
    /// no tick box anywhere to remove it. Observed on a real shop, whose stored set was
    /// <c>["CAD","JPY"]</c> against <c>["en-US","fr-FR"]</c>.
    ///
    /// This replaces an earlier rule that kept such a currency on purpose, so a branch would not
    /// "silently stop taking money it had said it takes". The intent was right and the mechanism was
    /// wrong: a tick nobody can see cannot be reviewed, confirmed or withdrawn, and the panel ended up
    /// returning something it did not show. It now returns exactly what it shows, and what stops a
    /// shop being left with nothing is <see cref="EnsureOneCurrency"/>. No ORDER is affected either
    /// way — an order records the currency it was priced in and never reads the shop's.
    /// </remarks>
    private List<CurrencyType> TickedCurrencies()
        => OfferedByTickedLanguages()
            .Where(currency => _currencyRows.TryGetValue(currency, out var row) && row.IsSupported)
            .ToList();

    /// <summary>
    /// Keeps the shop on at least one currency. Clearing the last tick is REPAIRED rather than
    /// refused: the red line says why, and the first currency the panel offers is re-ticked, so the
    /// panel is never sitting in a state that Done would have to reject.
    /// </summary>
    /// <remarks>
    /// A tick that springs back is normally a defect — one was fixed in this project for exactly that
    /// — and the difference is the message beside it. Springing back silently reads as a broken
    /// checkbox; springing back next to a red line naming the rule reads as the rule.
    ///
    /// When no language is ticked there is no currency to fall back TO, so it reports the missing
    /// LANGUAGE instead — the line then always describes the state the panel is actually in.
    /// <see cref="OnDoneClick"/> remains the last gate for both.
    /// </remarks>
    private void EnsureOneCurrency()
    {
        if (TickedCurrencies().Count > 0)
        {
            // Cleared with its text, and cleared as soon as the state is valid: a red line that
            // outlives the problem it described is read as a second problem. Inline rather than a
            // ClearError() helper — a method touching only x:Name fields is mis-flagged S2325 by
            // SonarLint's single-file pass, which cannot see the XAML-generated partial.
            ErrorText.Text = string.Empty;
            ErrorText.Visibility = Visibility.Collapsed;
            return;
        }

        var offered = OfferedByTickedLanguages();
        if (offered.Count == 0)
        {
            // No language ticked: there is nothing to fall back TO, and the missing language is the
            // real blocker. Naming it keeps the line describing the state it is actually in — leaving
            // the currency message up would have it explaining a problem that is no longer the one.
            ShowError("Shop.Setup.InstalledLanguagesRequired");
            return;
        }

        ShowError("Shop.Setup.SupportedCurrenciesRequired");

        _repairing = true;
        try
        {
            _currencyRows[offered[0]].IsSupported = true;
        }
        finally
        {
            _repairing = false;
        }
    }

    // ── result ────────────────────────────────────────────────────────────────────────────────

    private void OnDoneClick(object sender, RoutedEventArgs e)
    {
        var languages = _languages.Where(row => row.IsInstalled).Select(row => row.Code).ToList();
        if (languages.Count == 0)
        {
            ShowError("Shop.Setup.InstalledLanguagesRequired");
            return;
        }

        var currencies = TickedCurrencies();
        if (currencies.Count == 0)
        {
            ShowError("Shop.Setup.SupportedCurrenciesRequired");
            return;
        }

        InstalledLanguages = languages;
        SupportedCurrencies = currencies;

        // The pickers only ever contain ticked entries, so these are already valid — except on the
        // theoretical path where nothing is selected, which falls back to the first rather than
        // returning a shop that opens in a language it does not run in.
        PreferredLanguage = LanguageBox.SelectedValue as string ?? languages[0];
        PreferredCurrency = CurrencyBox.SelectedValue as CurrencyType? ?? currencies[0];

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ShowError(string key)
    {
        ErrorText.Text = _localization[key];
        ErrorText.Visibility = Visibility.Visible;
    }

    // ── rows ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>One shipped language, whether this shop runs in it, and what it brings.</summary>
    private sealed class LanguageRow : INotifyPropertyChanged
    {
        private bool _isInstalled;

        public LanguageRow(string code, string name, string currencySummary)
        {
            Code = code;
            Name = name;
            CurrencySummary = currencySummary;
        }

        public string Code { get; }

        [SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed",
            Justification = "Bound as Text=\"{Binding Name}\" in LanguageRowTemplate in " +
                            "ShopLocalizationWindow.xaml; XAML bindings are invisible to Roslyn.")]
        public string Name { get; }

        /// <summary>The currency codes this language brings, e.g. "CAD, USD" — shown under its name.</summary>
        [SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed",
            Justification = "Bound as Text=\"{Binding CurrencySummary}\" in LanguageRowTemplate in " +
                            "ShopLocalizationWindow.xaml; XAML bindings are invisible to Roslyn.")]
        public string CurrencySummary { get; }

        public bool IsInstalled
        {
            get => _isInstalled;
            set
            {
                if (_isInstalled == value)
                    return;

                _isInstalled = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsInstalled)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>One currency and whether the shop takes it. Shared across every language that brings it.</summary>
    private sealed class CurrencyRow : INotifyPropertyChanged
    {
        private bool _isSupported;

        public CurrencyRow(CurrencyType currency, string name, string symbol)
        {
            Currency = currency;
            Name = name;
            Symbol = symbol;
        }

        public CurrencyType Currency { get; }

        [SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed",
            Justification = "Bound as Text=\"{Binding Name}\" in CurrencyRowTemplate in " +
                            "ShopLocalizationWindow.xaml; XAML bindings are invisible to Roslyn.")]
        public string Name { get; }

        [SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed",
            Justification = "Bound as Text=\"{Binding Symbol}\" in CurrencyRowTemplate in " +
                            "ShopLocalizationWindow.xaml; XAML bindings are invisible to Roslyn.")]
        public string Symbol { get; }

        public bool IsSupported
        {
            get => _isSupported;
            set
            {
                if (_isSupported == value)
                    return;

                _isSupported = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSupported)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>One language's card on the right, holding the shared rows for what it brings.</summary>
    private sealed class CurrencyGroup
    {
        public CurrencyGroup(string languageName, IReadOnlyList<CurrencyRow> currencies)
        {
            LanguageName = languageName;
            Currencies = currencies;
            EmptyNoticeVisibility = currencies.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        [SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed",
            Justification = "Bound as Text=\"{Binding LanguageName}\" in CurrencyGroupTemplate in " +
                            "ShopLocalizationWindow.xaml; XAML bindings are invisible to Roslyn.")]
        public string LanguageName { get; }

        [SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed",
            Justification = "Bound as ItemsSource=\"{Binding Currencies}\" in CurrencyGroupTemplate in " +
                            "ShopLocalizationWindow.xaml; XAML bindings are invisible to Roslyn.")]
        public IReadOnlyList<CurrencyRow> Currencies { get; }

        [SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed",
            Justification = "Bound as Visibility=\"{Binding EmptyNoticeVisibility}\" in " +
                            "CurrencyGroupTemplate in ShopLocalizationWindow.xaml.")]
        public Visibility EmptyNoticeVisibility { get; }
    }
}
