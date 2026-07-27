using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Media;
using LeeYongeOrdering.Data;
using LeeYongeOrdering.Localization;
using LeeYongeOrdering.Models;
using LeeYongeOrdering.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LeeYongeOrdering.Views;

/// <summary>
/// Chooses the shop to work in. Runs at startup straight after sign-in, and again whenever the user
/// picks 本地配置 → 切换店铺.
///
/// Constructed by hand rather than through DI: on the startup path the generic host has been built
/// but not started, and this window is shown before the main window exists. It reads through the
/// scope factory it is handed, which is the same one every other shop-scoped read uses.
/// </summary>
public partial class ShopPickerWindow : Window
{
    private readonly LocalizationService _localization;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly UserAccount? _user;
    private readonly ObservableCollection<ShopRow> _rows = new();

    /// <param name="currentShop">
    /// The shop already open, preselected in the list. Null on the startup path.
    /// </param>
    public ShopPickerWindow(
        LocalizationService localization,
        IServiceScopeFactory scopeFactory,
        UserAccount? user,
        Shop? currentShop)
    {
        InitializeComponent();

        _localization = localization;
        _scopeFactory = scopeFactory;
        _user = user;

        ShopList.ItemsSource = _rows;

        // Creating a shop is an administrator's job. Hidden rather than disabled: a greyed-out
        // button invites a support call, an absent one reads as "not your job".
        CreateButton.Visibility = AuthenticationService.Instance.CanManageShops
            ? Visibility.Visible
            : Visibility.Collapsed;

        SignedInText.Text = BuildSignedInText();

        LoadShops(currentShop?.Id);
    }

    /// <summary>The chosen shop, or null when the window was closed without choosing one.</summary>
    public Shop? SelectedShop { get; private set; }

    /// <summary>
    /// Set when a newly created shop asked for its measurement terms to be configured. The window
    /// cannot do it itself: MeasurementTermsService edits whichever shop is BOUND, and the new shop
    /// is not bound until the caller opens it. The caller therefore opens the terms editor after
    /// the shop is active — see the callers in App and MainWindow.
    /// </summary>
    public bool ConfigureTermsRequested { get; private set; }

    private string BuildSignedInText()
    {
        if (_user is null)
            return string.Empty;

        var role = _localization[RoleKey(_user.Role)];
        return _localization.Format("Shop.Picker.SignedInAs", _user.UserName, role);
    }

    private static string RoleKey(UserRole role) => role switch
    {
        UserRole.Admin => "Shop.Role.Admin",
        UserRole.Manager => "Shop.Role.Manager",
        _ => "Shop.Role.Staff"
    };

    private void LoadShops(int? preselectShopId)
    {
        _rows.Clear();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var shops = db.Shops
            .AsNoTracking()
            .Where(shop => !shop.IsArchived)
            .OrderBy(shop => shop.Id)
            .ToList();

        // IgnoreQueryFilters is essential: AppDbContext filters Orders to the ACTIVE shop, so
        // without it every shop in this list would report the open shop's order count (and zero on
        // the startup path, where no shop is active yet).
        var counts = db.Orders
            .IgnoreQueryFilters()
            .GroupBy(order => order.ShopId)
            .Select(group => new { ShopId = group.Key, Count = group.Count() })
            .ToDictionary(entry => entry.ShopId, entry => entry.Count);

        foreach (var shop in shops)
            _rows.Add(new ShopRow(shop, BuildDetails(shop, counts.GetValueOrDefault(shop.Id)), _localization));

        EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        ShopList.SelectedItem =
            _rows.FirstOrDefault(row => row.Shop.Id == preselectShopId) ?? _rows.FirstOrDefault();

        UpdateOpenButtonState();
    }

    private string BuildDetails(Shop shop, int orderCount)
    {
        var currency = _localization[$"CurrencyType.{shop.CurrencyType}"];

        var language = _localization.AvailableLanguages
            .FirstOrDefault(option => option.Code == shop.PreferredLanguageCode)?.Name
            ?? shop.PreferredLanguageCode
            ?? string.Empty;

        var orders = _localization.Format("Shop.Picker.OrderCount", orderCount);

        // Blank segments are dropped rather than left as stray separators — a shop with no
        // preferred language would otherwise render "CAD ·  · 3 orders".
        var parts = new[] { currency, language, orders }
            .Where(part => !string.IsNullOrWhiteSpace(part));

        return string.Join("  ·  ", parts);
    }

    private void UpdateOpenButtonState()
        => OpenButton.IsEnabled = ShopList.SelectedItem is ShopRow;

    private void OnShopSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        => UpdateOpenButtonState();

    private void OnShopDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Only when a row was hit — double-clicking the empty area below the last item leaves the
        // previous selection in place and would otherwise open a shop the user never clicked.
        if (e.OriginalSource is DependencyObject source && IsWithinListItem(source))
            Confirm();
    }

    /// <summary>Whether a hit-tested element sits inside a row rather than the list's blank space.</summary>
    private bool IsWithinListItem(DependencyObject source)
    {
        for (var node = source; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is System.Windows.Controls.ListBoxItem)
                return true;

            if (ReferenceEquals(node, ShopList))
                return false;
        }

        return false;
    }

    private void OnOpenClick(object sender, RoutedEventArgs e) => Confirm();

    private void Confirm()
    {
        if (ShopList.SelectedItem is not ShopRow row)
            return;

        SelectedShop = row.Shop;
        DialogResult = true;
    }

    private void OnCreateClick(object sender, RoutedEventArgs e)
    {
        // Defence in depth: the button is hidden for non-administrators, but the check belongs
        // where the action happens, not only where it is offered.
        if (!AuthenticationService.Instance.CanManageShops)
            return;

        var setup = new ShopSetupWindow(_localization, _scopeFactory) { Owner = this };
        if (setup.ShowDialog() is not true || setup.Shop is null)
            return;

        // A shop you just created is the one you want to work in, so this closes the picker rather
        // than dropping the user back into a list to hunt for it.
        SelectedShop = setup.Shop;
        ConfigureTermsRequested = setup.ConfigureTermsRequested;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>One shop as the list renders it.</summary>
    private sealed class ShopRow
    {
        private readonly LocalizationService _localization;

        public ShopRow(Shop shop, string details, LocalizationService localization)
        {
            Shop = shop;
            Details = details;
            _localization = localization;
        }

        public Shop Shop { get; }

        // Consumed by {Binding Name} in the picker's item template, which Roslyn cannot see, so
        // every analyzer reads it as dead. Deleting it blanks the shop name in the list.
        [SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed",
            Justification = "Bound from ShopPickerWindow.xaml; XAML data bindings are invisible to static analysis.")]
        public string Name => Shop.ResolveName(_localization.CurrentLanguageCode);

        public string Details { get; }
    }
}
