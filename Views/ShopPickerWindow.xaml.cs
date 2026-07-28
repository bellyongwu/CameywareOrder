using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using CameywareOrder.Data;
using CameywareOrder.Localization;
using CameywareOrder.Models;
using CameywareOrder.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CameywareOrder.Views;

/// <summary>
/// Chooses the shop to work in. Runs at startup straight after sign-in, and again whenever the user
/// picks 本地配置 → 切换店铺. For an administrator it is also the way into 用户管理 — this is the one
/// screen where "which shops exist" and "who may open them" are both on the table.
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
    private readonly ObservableCollection<ShopPickerRow> _rows = new();

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

        // Creating a shop and managing accounts are an administrator's job. Hidden rather than
        // disabled: a greyed-out button invites a support call, an absent one reads as "not your job".
        var adminVisibility = AuthenticationService.Instance.IsAdministrator
            ? Visibility.Visible
            : Visibility.Collapsed;
        CreateButton.Visibility = adminVisibility;
        ManageUsersButton.Visibility = adminVisibility;

        ApplySignedInHeader();

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

    private void ApplySignedInHeader()
    {
        if (_user is null)
        {
            SignedInText.Text = string.Empty;
            SignedInRoleText.Text = string.Empty;
            UserInitialText.Text = string.Empty;
            return;
        }

        SignedInText.Text = _localization.Format("Shop.Picker.SignedInUser", _user.UserName);
        UserInitialText.Text = UserPresentation.Initial(_user.UserName);

        // No shop is open yet, so there is no single role to report: an administrator is one
        // everywhere, and everyone else holds a role PER shop — which is what the row badges show.
        SignedInRoleText.Text = BuildAccessSummary(_user);
    }

    private string BuildAccessSummary(UserAccount user)
    {
        if (user.IsAdministrator)
            return _localization["Shop.Role.Admin"];

        var shopCount = CountAccessibleShops(user);

        // "0 shops" is a count where the user needs a statement — it is the whole reason the list
        // below is empty.
        return shopCount == 0
            ? _localization["Users.NoAccess"]
            : _localization.Format("Users.ShopCount", shopCount);
    }

    // Active memberships only: a shop that has delisted this person is not one they can open, so
    // counting it here would promise access the list below does not offer.
    private static int CountAccessibleShops(UserAccount user)
        => user.Memberships.Count(membership => membership.IsActive);

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

        // A user only ever sees the shops they hold a role in; an administrator sees every one.
        // Filtered here as well as in App.LoadSelectableShopsAsync because this window is also
        // reached from 切换店铺, which does not go through that path.
        var existingShopCount = shops.Count;
        shops = AuthenticationService.Instance.FilterAccessibleShops(shops);

        // IgnoreQueryFilters is essential: AppDbContext filters Orders to the ACTIVE shop, so
        // without it every shop in this list would report the open shop's order count (and zero on
        // the startup path, where no shop is active yet).
        var counts = db.Orders
            .IgnoreQueryFilters()
            .GroupBy(order => order.ShopId)
            .Select(group => new { ShopId = group.Key, Count = group.Count() })
            .ToDictionary(entry => entry.ShopId, entry => entry.Count);

        foreach (var shop in shops)
        {
            _rows.Add(new ShopPickerRow(
                shop,
                shop.ResolveName(_localization.CurrentLanguageCode),
                BuildDetails(shop, counts.GetValueOrDefault(shop.Id)),
                AuthenticationService.Instance.RoleFor(shop.PublicId),
                _localization));
        }

        // Two different empty states, and only one of them is the user's to act on: an installation
        // with no shops at all wants "create one", while a list emptied by the assignment filter
        // wants "ask an administrator".
        EmptyText.Text = _localization[existingShopCount == 0
            ? "Shop.Picker.Empty"
            : "Shop.Picker.NoShopForRole"];
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

        return _localization.JoinFragments(parts);
    }

    private void UpdateOpenButtonState()
        => OpenButton.IsEnabled = ShopList.SelectedItem is ShopPickerRow;

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
        if (ShopList.SelectedItem is not ShopPickerRow row)
            return;

        SelectedShop = row.Shop;
        DialogResult = true;
    }

    private void OnCreateClick(object sender, RoutedEventArgs e)
    {
        // Defence in depth: the button is hidden for non-administrators, but the check belongs
        // where the action happens, not only where it is offered.
        if (!AuthenticationService.Instance.CanCreateShops)
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

    private void OnManageUsersClick(object sender, RoutedEventArgs e)
    {
        if (!AuthenticationService.Instance.CanManageUsers)
            return;

        new UserManagementWindow(_localization, _scopeFactory) { Owner = this }.ShowDialog();

        // Reloaded because an administrator can revoke their OWN access to a shop here. Keeping the
        // stale list would offer a shop that the next click is no longer allowed to open.
        LoadShops((ShopList.SelectedItem as ShopPickerRow)?.Shop.Id);
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}

/// <summary>
/// One shop as the picker's card template renders it.
/// </summary>
/// <remarks>
/// Deliberately a top-level internal type rather than a private nested one. Every member here is
/// reached only through <c>{Binding}</c>, which static analysis cannot see, so as private members
/// they all read as dead code and each needs its own suppression to stay. Internal members are not
/// in that rule's scope, which resolves the problem instead of annotating around it.
/// </remarks>
internal sealed class ShopPickerRow
{
    private static readonly Brush AdminBadgeBackground = Frozen("#E0E7FF");
    private static readonly Brush AdminBadgeForeground = Frozen("#3730A3");
    private static readonly Brush ManagerBadgeBackground = Frozen("#FEF3C7");
    private static readonly Brush ManagerBadgeForeground = Frozen("#92400E");
    private static readonly Brush StaffBadgeBackground = Frozen("#D1FAE5");
    private static readonly Brush StaffBadgeForeground = Frozen("#065F46");
    private static readonly Brush NoRoleBadgeBackground = Frozen("#F3F4F6");
    private static readonly Brush NoRoleBadgeForeground = Frozen("#6B7280");

    public ShopPickerRow(Shop shop, string name, string details, UserRole? role, LocalizationService localization)
    {
        Shop = shop;
        Name = name;
        Details = details;
        Initial = UserPresentation.Initial(name);
        AvatarBrush = UserPresentation.AvatarBrush(name);

        RoleText = UserPresentation.RoleText(localization, role);
        RoleBackground = BadgeBackground(role);
        RoleForeground = BadgeForeground(role);
    }

    public Shop Shop { get; }

    public string Name { get; }

    public string Details { get; }

    public string Initial { get; }

    public Brush AvatarBrush { get; }

    public string RoleText { get; }

    public Brush RoleBackground { get; }

    public Brush RoleForeground { get; }

    private static Brush BadgeBackground(UserRole? role) => role switch
    {
        UserRole.Admin => AdminBadgeBackground,
        UserRole.Manager => ManagerBadgeBackground,
        UserRole.Staff => StaffBadgeBackground,
        _ => NoRoleBadgeBackground
    };

    private static Brush BadgeForeground(UserRole? role) => role switch
    {
        UserRole.Admin => AdminBadgeForeground,
        UserRole.Manager => ManagerBadgeForeground,
        UserRole.Staff => StaffBadgeForeground,
        _ => NoRoleBadgeForeground
    };

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
