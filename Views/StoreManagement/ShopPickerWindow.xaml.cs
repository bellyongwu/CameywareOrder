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
/// picks Local Configuration → Switch Shop. For an administrator it is also the way into User
/// Management — this is the one screen where "which shops exist" and "who may open them" are both
/// on the table.
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

        // Managing shops and accounts is an administrator's job. Hidden rather than disabled: a
        // greyed-out button invites a support call, an absent one reads as "not your job".
        var adminVisibility = AuthenticationService.Instance.IsAdministrator
            ? Visibility.Visible
            : Visibility.Collapsed;
        ManageUsersButton.Visibility = adminVisibility;
        StoreManagementButton.Visibility = adminVisibility;

        // Its own capability rather than the blanket administrator flag, because it IS the screen
        // where that distinction is made — gating it on anything else would be the panel exempting
        // itself from the model it defines.
        PermissionsButton.Visibility = AuthenticationService.Instance.CanManagePermissions
            ? Visibility.Visible
            : Visibility.Collapsed;

        // No ApplySignedInHeader() here: LoadShops ends with it, because the header counts the rows
        // LoadShops builds.
        LoadShops(currentShop?.Id);
    }

    /// <summary>The chosen shop, or null when the window was closed without choosing one.</summary>
    public Shop? SelectedShop { get; private set; }

    /// <summary>
    /// Set when the administrator used User Management to sign in as somebody else. Distinct from a
    /// CANCELLED picker, which means "sign out": here the session simply belongs to a different
    /// person and the picker has to run again for them.
    /// </summary>
    public string? SignInAsUserName { get; private set; }

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

    /// <summary>
    /// "Three shops" beside the signed-in name — the count of the cards below it.
    /// </summary>
    /// <remarks>
    /// Counted from <see cref="_rows"/> rather than from the account's memberships, which is why
    /// <see cref="ApplySignedInHeader"/> runs at the END of <see cref="LoadShops"/>. Counting the
    /// memberships answered a subtly different question and got it wrong twice over: a membership of
    /// a shop DELETED before v9.5.1 survives in `credentials.json` and was counted (one shop, "three
    /// shops"), and the header was written once in the constructor, so deleting a shop from Store
    /// Management shrank the list and left the number behind.
    ///
    /// Both stop being possible when the number is read off the thing it describes.
    /// </remarks>
    private string BuildAccessSummary(UserAccount user)
    {
        if (user.IsAdministrator)
            return _localization["Shop.Role.Admin"];

        // "0 shops" is a count where the user needs a statement — it is the whole reason the list
        // below is empty.
        return _rows.Count == 0
            ? _localization["Users.NoAccess"]
            : _localization.Format("Users.ShopCount", _rows.Count);
    }

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
        // reached from Switch Shop, which does not go through that path.
        var existingShopCount = shops.Count;
        shops = AuthenticationService.Instance.FilterAccessibleShops(shops);

        // IgnoreQueryFilters is essential: AppDbContext filters Orders to the ACTIVE shop, so
        // without it every shop in this list would report the open shop's order count (and zero on
        // the startup path, where no shop is active yet). It also drops the recycle-bin condition,
        // so that one is restated — a card saying "12 orders" must mean the twelve the branch's list
        // will actually show.
        var counts = db.Orders
            .IgnoreQueryFilters()
            .Where(order => order.DeletedOnUtc == null)
            .GroupBy(order => order.ShopId)
            .Select(group => new { ShopId = group.Key, Count = group.Count() })
            .ToDictionary(entry => entry.ShopId, entry => entry.Count);

        foreach (var shop in shops)
        {
            _rows.Add(new ShopPickerRow(
                shop,
                shop.ResolveName(_localization.CurrentLanguageCode),
                BuildDetails(shop, counts.GetValueOrDefault(shop.Id)),
                AuthenticationService.Instance.RolesFor(shop.PublicId),
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

        // Last, and inside LoadShops rather than beside its callers: the header reports how many of
        // these rows there are, and three of the four reloads (Store Management, a language change,
        // a new shop) can change that number. See BuildAccessSummary.
        ApplySignedInHeader();
    }

    /// <summary>
    /// The metadata strip under a shop's name: currency, the languages it runs in, order count.
    /// </summary>
    /// <remarks>
    /// The languages are the shop's INSTALLED set, not just the one it opens in. This card is where
    /// somebody decides which branch to work in, and for anyone but an administrator the installed
    /// set is exactly the set they will be able to switch between once inside — so it answers a
    /// question the preferred language alone could not. It also cannot mislead the way one name did:
    /// a bilingual shop used to advertise a single language here.
    ///
    /// Plain text rather than a row of chips deliberately. Languages are DISCOVERED — an
    /// installation can ship any number — and a line that ellipsizes degrades predictably where a
    /// growing stack of badges would change the card's height for everyone.
    ///
    /// Two different joins on one line, and that is the intended distinction: JoinList punctuates
    /// the languages as prose ("Chinese, English"), JoinFragments separates the strip's fields.
    /// </remarks>
    private string BuildDetails(Shop shop, int orderCount)
    {
        // The currencies the branch ACCEPTS, not just the one it prices in by default — the same
        // change the language slot got, and for the same reason: the set is strictly more
        // informative, and for a single-currency shop the two read identically anyway.
        var currency = _localization.JoinList(
            ShopCurrencies.Supported(shop).Select(item => ShopCurrencies.Name(item, _localization)));

        var languages = _localization.JoinList(
            ShopLanguages.Installed(shop, _localization).Select(option => option.Name));

        var orders = _localization.Format("Shop.Picker.OrderCount", orderCount);

        // Blank segments are dropped rather than left as stray separators, which would render
        // "CAD ·  · 3 orders". ShopLanguages.Installed never comes back empty, so the languages
        // cannot be the blank one — currency and the count are what this still guards.
        var parts = new[] { currency, languages, orders }
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

    /// <summary>
    /// Opens the permission panel from the picker.
    /// </summary>
    /// <remarks>
    /// The shop list is rebuilt afterwards, and that is not cosmetic: a role's capabilities decide
    /// nothing about which shops are OFFERED, but withdrawing somebody's last role in a branch does,
    /// and the administrator can do exactly that from the panel — including to themselves.
    /// </remarks>
    private void OnPermissionsClick(object sender, RoutedEventArgs e)
    {
        if (!AuthenticationService.Instance.CanManagePermissions)
            return;

        new PermissionsWindow(_localization, _scopeFactory) { Owner = this }.ShowDialog();

        LoadShops((ShopList.SelectedItem as ShopPickerRow)?.Shop.Id);
    }

    private void OnManageUsersClick(object sender, RoutedEventArgs e)
    {
        if (!AuthenticationService.Instance.CanManageUsers)
            return;

        var users = new UserManagementWindow(_localization, _scopeFactory) { Owner = this };
        users.ShowDialog();

        // "Sign in as this user" changes who this picker is FOR, and its whole list is the previous
        // user's accessible shops. Reported up and closed rather than reloaded in place: App runs
        // the picker in a loop and will build a fresh one for whoever the session now belongs to.
        if (users.SignInAsUserName is { } userName)
        {
            SignInAsUserName = userName;
            Close();
            return;
        }

        // Reloaded because an administrator can revoke their OWN access to a shop here. Keeping the
        // stale list would offer a shop that the next click is no longer allowed to open.
        LoadShops((ShopList.SelectedItem as ShopPickerRow)?.Shop.Id);
    }

    /// <summary>
    /// Store Management: create, copy, delist, delete, download, restore, reinitialise. Administrator
    /// only, and gated again in the service — the button being hidden is presentation, not
    /// authorisation.
    /// </summary>
    /// <remarks>
    /// A shop CREATED there comes back as <c>CreatedShop</c> and is selected here, because a shop you
    /// have just made is the one you meant to work in. It does not close the picker the way the old
    /// Create button did: the administrator may well have gone in to create two branches, or to
    /// create one and delist another, and slamming the window shut on the first of those is a worse
    /// guess than leaving them one click from Open.
    /// </remarks>
    private void OnStoreManagementClick(object sender, RoutedEventArgs e)
    {
        if (!AuthenticationService.Instance.IsAdministrator)
            return;

        var management = new StoreManagementWindow(_localization, _scopeFactory) { Owner = this };
        management.ShowDialog();

        // Carried through even when nothing else changed: it belongs to whoever eventually OPENS the
        // new shop, which is a later step than this one.
        if (management.ConfigureTermsRequested)
            ConfigureTermsRequested = true;

        // Only when something actually happened. A shop may have been created, copied, deleted,
        // restored or delisted, and this list would otherwise offer one that no longer exists, or
        // omit one that now does.
        if (!management.ShopsChanged)
            return;

        LoadShops(management.CreatedShop?.Id ?? (ShopList.SelectedItem as ShopPickerRow)?.Shop.Id);
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

    public ShopPickerRow(
        Shop shop, string name, string details, IReadOnlyList<RoleDefinition> roles,
        LocalizationService localization)
    {
        Shop = shop;
        Name = name;
        Details = details;
        Initial = UserPresentation.Initial(name);
        AvatarBrush = UserPresentation.AvatarBrush(name);

        RoleText = UserPresentation.RoleList(localization, roles);

        var standing = Standing(roles);
        RoleBackground = BadgeBackground(standing);
        RoleForeground = BadgeForeground(standing);
    }

    public Shop Shop { get; }

    public string Name { get; }

    public string Details { get; }

    public string Initial { get; }

    public Brush AvatarBrush { get; }

    public string RoleText { get; }

    public Brush RoleBackground { get; }

    public Brush RoleForeground { get; }

    /// <summary>
    /// How much standing this card's roles add up to, for the badge colour.
    /// </summary>
    /// <remarks>
    /// Read from the CAPABILITIES rather than from the role's name or id, because an installation
    /// defines its own roles and a colour keyed on "manager" would leave every one of them the same
    /// anonymous grey. Whether the person can change how the shop runs is the distinction the badge
    /// was always drawing; now it asks that question directly.
    /// </remarks>
    private static ShopStanding Standing(IReadOnlyList<RoleDefinition> roles)
    {
        if (roles.Count == 0)
            return ShopStanding.None;

        if (roles.Any(role => role.IsAdministratorRole))
            return ShopStanding.Administrator;

        return roles.Any(role => role.Grants(AppCapability.ConfigureShop))
            ? ShopStanding.Runs
            : ShopStanding.Works;
    }

    private static Brush BadgeBackground(ShopStanding standing) => standing switch
    {
        ShopStanding.Administrator => AdminBadgeBackground,
        ShopStanding.Runs => ManagerBadgeBackground,
        ShopStanding.Works => StaffBadgeBackground,
        _ => NoRoleBadgeBackground
    };

    private static Brush BadgeForeground(ShopStanding standing) => standing switch
    {
        ShopStanding.Administrator => AdminBadgeForeground,
        ShopStanding.Runs => ManagerBadgeForeground,
        ShopStanding.Works => StaffBadgeForeground,
        _ => NoRoleBadgeForeground
    };

    /// <summary>What a person's roles amount to in one shop, as far as the badge is concerned.</summary>
    private enum ShopStanding
    {
        None,
        Works,
        Runs,
        Administrator
    }

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
