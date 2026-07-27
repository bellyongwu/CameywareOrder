using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using LeeYongeOrdering.Data;
using LeeYongeOrdering.Localization;
using LeeYongeOrdering.Models;
using LeeYongeOrdering.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LeeYongeOrdering.Views;

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

        var languageCode = LanguageBox.SelectedValue as string;
        var currencyType = CurrencyBox.SelectedValue as CurrencyType? ?? CurrencyType.CAD;

        Shop = _existing is null
            ? CreateShop(names, languageCode, currencyType)
            : UpdateShop(names, languageCode, currencyType);

        DialogResult = true;
    }

    private Shop CreateShop(Dictionary<string, string> names, string? languageCode, CurrencyType currencyType)
    {
        var shop = new Shop
        {
            PublicId = Guid.NewGuid(),
            PreferredLanguageCode = languageCode,
            CurrencyType = currencyType,
            CreatedAtUtc = DateTime.UtcNow
        };
        shop.SetNames(names);

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

    private Shop UpdateShop(Dictionary<string, string> names, string? languageCode, CurrencyType currencyType)
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
}
