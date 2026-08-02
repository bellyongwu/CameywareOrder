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
/// Administrator-only screen for what the application's roles mean, and who holds them. Reached from
/// the shop picker and from Local Configuration → Permissions.
///
/// The permission model used to be three fixed roles compared against in code, so an installation
/// that wanted somebody who reads the settlement report and touches nothing else had no way to say
/// so. This screen is where that is now said.
/// </summary>
/// <remarks>
/// TWO TREES, TWO QUESTIONS. The left assigns roles to people per shop; the right defines what a
/// role may do. They are deliberately not merged into one per-person permission list: a screen where
/// each of forty people carries their own set of nineteen tick boxes cannot be audited, and the
/// first thing anybody would ask of it — "who can delete an order" — would have no answer short of
/// reading all forty.
///
/// Roles are defined ONCE for the whole installation, so the same role node is shared by every shop
/// that lists it (see <see cref="RoleNode"/>). Editing the Auditor under one branch is editing it
/// everywhere, and the screen shows that rather than hiding it.
///
/// EVERYTHING IS WRITTEN ON SAVE, never per tick. A permission panel that saved as it went would
/// revoke a manager's access halfway through being re-graded — and the administrator doing it might
/// be revoking their own.
/// </remarks>
public partial class PermissionsWindow : Window
{
    private readonly LocalizationService _localization;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly List<Shop> _shops;

    private List<AccountNode> _accounts = new();
    private List<RoleNode> _roles = new();

    public PermissionsWindow(LocalizationService localization, IServiceScopeFactory scopeFactory)
    {
        InitializeComponent();

        _localization = localization;
        _scopeFactory = scopeFactory;
        _shops = LoadShops();

        Rebuild(selectRoleId: null);
    }

    /// <summary>
    /// Every shop, archived ones included — an archived shop can still hold an assignment, and a row
    /// that is not on screen is an assignment nobody can withdraw.
    /// </summary>
    private List<Shop> LoadShops()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return db.Shops.AsNoTracking().OrderBy(shop => shop.Id).ToList();
    }

    /// <summary>Rebuilds both trees from the catalog and the account file.</summary>
    private void Rebuild(string? selectRoleId)
    {
        _roles = BuildRoleNodes();
        _accounts = BuildAccountNodes();

        // One shared set of role nodes under every shop, so a tick is the same tick everywhere.
        ShopTree.ItemsSource = _shops
            .Select(shop => new ShopNode(
                shop.ResolveName(_localization.CurrentLanguageCode),
                BuildShopDetails(shop),
                _roles))
            .ToList();

        AccountTree.ItemsSource = _accounts;

        ShowRoleActions(selectRoleId is null
            ? null
            : _roles.Find(role => string.Equals(role.RoleId, selectRoleId, StringComparison.OrdinalIgnoreCase)));
    }

    private string BuildShopDetails(Shop shop)
    {
        var members = AuthenticationService.Instance.ListMembers(shop.PublicId).Count;
        var label = _localization.Format("Permission.ShopMembers", members);

        return shop.IsArchived
            ? _localization.JoinList(new[] { label, _localization["Users.ArchivedShop"] })
            : label;
    }

    /// <summary>
    /// One node per role, each carrying every capability the application has — ticked where the role
    /// grants it, and disabled where nobody may be given it.
    /// </summary>
    private List<RoleNode> BuildRoleNodes()
    {
        var auth = AuthenticationService.Instance;

        return RolePermissionStore.Instance.All()
            .Select(role =>
            {
                var groups = CapabilityCatalog.Groups
                    .Select(group => new CapabilityGroupNode(
                        _localization[CapabilityCatalog.GroupNameKey(group)],
                        CapabilityCatalog.InGroup(group)
                            .Select(entry => new CapabilityNode(
                                entry,
                                _localization,
                                role.Grants(entry.Capability),
                                // The administrator's own set is fixed, and the three
                                // administrator-only capabilities are fixed for everyone else.
                                !role.IsLocked && !entry.AdministratorOnly))
                            .ToList()))
                    .ToList();

                var node = new RoleNode(role, _localization, auth.HoldersOf(role.Id), groups);

                foreach (var capability in groups.SelectMany(group => group.Capabilities))
                    capability.Owner = node;

                return node;
            })
            .ToList();
    }

    /// <summary>One node per account, with its roles in each shop as tick boxes.</summary>
    /// <remarks>
    /// The administrator gets no tick boxes at all. Their rights are an account flag rather than a
    /// membership, so a box here would be a control that cannot change anything — and the honest way
    /// to say "this is not a decision" is to not draw the decision.
    /// </remarks>
    private List<AccountNode> BuildAccountNodes()
    {
        return AuthenticationService.Instance.ListAccounts()
            .Select(account => new AccountNode(
                account.UserName,
                account.DisplayLabel,
                BuildAccountDetails(account),
                account.IsAdministrator,
                account.IsAdministrator
                    ? Array.Empty<AccountShopNode>()
                    : _shops.Select(shop => new AccountShopNode(
                        shop.PublicId,
                        shop.ResolveName(_localization.CurrentLanguageCode),
                        RoleToggle.ForMembership(
                            _localization,
                            account.Memberships
                                .FirstOrDefault(membership => membership.ShopPublicId == shop.PublicId)
                                ?.RoleIds)))
                        .ToList()))
            .ToList();
    }

    private string BuildAccountDetails(UserAccount account)
        => account.IsAdministrator
            ? _localization["Shop.Role.Admin"]
            : UserPresentation.RoleList(_localization, account.HeldRoles());

    // --- Role actions ---------------------------------------------------------------------------

    private void OnShopTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        => ShowRoleActions(e.NewValue as RoleNode);

    /// <summary>
    /// Points the action bar at a role, or hides it when the selection is not one.
    /// </summary>
    /// <remarks>
    /// Delete is DISABLED rather than hidden for a shipped role, unlike the bar as a whole: "this
    /// role cannot be removed" is a fact worth showing, while a bar acting on nothing is not.
    /// </remarks>
    private void ShowRoleActions(RoleNode? role)
    {
        if (role is null)
        {
            RoleActionBar.Visibility = Visibility.Collapsed;
            return;
        }

        RoleActionBar.Visibility = Visibility.Visible;
        RoleActionBar.Tag = role.RoleId;
        SelectedRoleLabel.Text = _localization["Permission.SelectedRole"];
        RoleNameBox.Text = role.Name;

        RoleNameBox.IsEnabled = role.IsEditable;
        RenameRoleButton.IsEnabled = role.IsEditable;
        RestoreRoleButton.IsEnabled = role.IsBuiltIn && role.IsEditable;
        DeleteRoleButton.IsEnabled = role.IsEditable && !role.IsBuiltIn;
    }

    private string? SelectedRoleId => RoleActionBar.Tag as string;

    private void OnAddRoleClick(object sender, RoutedEventArgs e)
    {
        // A new role starts EMPTY rather than copying an existing one. A permission granted because
        // it happened to be on the role somebody cloned is the permission nobody can explain later.
        var result = RolePermissionStore.Instance.Create(
            _localization, NewRoleNameBox.Text, Array.Empty<AppCapability>(), out var createdId);

        if (result != RoleOperationResult.Success)
        {
            ShowError(result);
            return;
        }

        NewRoleNameBox.Clear();
        Rebuild(createdId);
        ShowStatus("Permission.Created");
    }

    private void OnRenameRoleClick(object sender, RoutedEventArgs e)
    {
        if (SelectedRoleId is not { } roleId)
            return;

        var result = RolePermissionStore.Instance.Rename(_localization, roleId, RoleNameBox.Text);

        if (result != RoleOperationResult.Success)
        {
            ShowError(result);
            return;
        }

        Rebuild(roleId);
        ShowStatus("Permission.Renamed");
    }

    private void OnRestoreRoleClick(object sender, RoutedEventArgs e)
    {
        if (SelectedRoleId is not { } roleId)
            return;

        var result = RolePermissionStore.Instance.RestoreDefaults(roleId);

        if (result != RoleOperationResult.Success)
        {
            ShowError(result);
            return;
        }

        Rebuild(roleId);
        ShowStatus("Permission.Restored");
    }

    /// <summary>
    /// Removes a role, after saying how many people it is being taken away from.
    /// </summary>
    /// <remarks>
    /// The count is the whole point of the confirmation. "Delete this role?" is a question about a
    /// list; "this removes it from four people" is a question about the shop — and they get
    /// different answers.
    /// </remarks>
    private void OnDeleteRoleClick(object sender, RoutedEventArgs e)
    {
        if (SelectedRoleId is not { } roleId)
            return;

        var role = _roles.Find(candidate =>
            string.Equals(candidate.RoleId, roleId, StringComparison.OrdinalIgnoreCase));

        if (role is null)
            return;

        var prompt = role.Holders == 0
            ? _localization.Format("Permission.Confirm.Delete", role.Name)
            : _localization.Format("Permission.Confirm.DeleteHeld", role.Name, role.Holders);

        var answer = MessageBox.Show(
            this, prompt, _localization["Toolbar.Permissions"],
            MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
            return;

        var result = RolePermissionStore.Instance.Delete(roleId);

        if (result != RoleOperationResult.Success)
        {
            ShowError(result);
            return;
        }

        Rebuild(selectRoleId: null);
        ShowStatus("Permission.Deleted");
    }

    // --- Save -----------------------------------------------------------------------------------

    /// <summary>
    /// Writes both trees: what each role may do, and who holds which role where.
    /// </summary>
    /// <remarks>
    /// The role definitions go first. An account can only be given roles that exist, so saving the
    /// assignments against a catalog that had not been updated yet would write ids the store was
    /// about to change underneath them.
    /// </remarks>
    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var store = RolePermissionStore.Instance;

        foreach (var role in _roles.Where(role => role.IsEditable))
            store.SetCapabilities(role.RoleId, role.Selected());

        var auth = AuthenticationService.Instance;

        foreach (var account in _accounts.Where(account => !account.IsAdministrator))
        {
            var rolesByShop = account.Shops.ToDictionary(
                shop => shop.ShopPublicId,
                shop => (IReadOnlyList<string>)RoleToggle.Selected(shop.Roles));

            auth.SetShopRoles(account.UserName, rolesByShop);
        }

        Rebuild(SelectedRoleId);
        ShowStatus("Permission.Saved");
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void ShowStatus(string key)
    {
        StatusText.Foreground = DoneBrush;
        StatusText.Text = _localization[key];
    }

    /// <summary>
    /// A refusal, in the same place as a confirmation but never in the same colour: one line that
    /// says "saved" in green and "that name is taken" in green would be read as the first.
    /// </summary>
    private void ShowError(RoleOperationResult result)
    {
        StatusText.Foreground = RefusedBrush;
        StatusText.Text = _localization[result switch
        {
            RoleOperationResult.NameRequired => "Permission.Error.NameRequired",
            RoleOperationResult.NameTaken => "Permission.Error.NameTaken",
            RoleOperationResult.Protected => "Permission.Error.Protected",
            _ => "Permission.Error.NotFound"
        }];
    }

    private static readonly System.Windows.Media.Brush DoneBrush = Frozen("#047857");

    private static readonly System.Windows.Media.Brush RefusedBrush = Frozen("#B91C1C");

    private static System.Windows.Media.Brush Frozen(string hex)
    {
        var brush = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
