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
/// Administrator-only screen for accounts and what each of them may open. Reached from the shop
/// picker and from 本地配置 → 用户管理.
///
/// Accounts come from <see cref="AuthenticationService"/> (a file outside the database) while the
/// shops they are assigned to come from the database, so this window needs both. It edits ONE
/// account at a time and writes on 保存修改: an assignment matrix that saved on every tick would
/// revoke access halfway through a re-assignment.
/// </summary>
public partial class UserManagementWindow : Window
{
    private readonly LocalizationService _localization;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ObservableCollection<UserListRow> _users = new();
    private readonly ObservableCollection<ShopAssignmentRow> _assignments = new();
    private readonly List<Shop> _shops;

    // Selecting an item programmatically raises SelectionChanged, and clearing the list raises it
    // with a null selection — both would repaint the detail pane mid-rebuild.
    private bool _isReloading;

    public UserManagementWindow(LocalizationService localization, IServiceScopeFactory scopeFactory)
    {
        InitializeComponent();

        _localization = localization;
        _scopeFactory = scopeFactory;

        UserList.ItemsSource = _users;
        AssignmentList.ItemsSource = _assignments;

        _shops = LoadShops();

        ReloadUsers(selectUserName: null);
    }

    /// <summary>
    /// Every shop, archived ones included. An archived shop can still hold an assignment, and
    /// hiding the row would silently strip that assignment the next time the account was saved.
    /// </summary>
    private List<Shop> LoadShops()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return db.Shops
            .AsNoTracking()
            .OrderBy(shop => shop.Id)
            .ToList();
    }

    private void ReloadUsers(string? selectUserName)
    {
        _isReloading = true;
        try
        {
            var filter = SearchBox.Text.Trim();

            _users.Clear();

            var matches = AuthenticationService.Instance.ListAccounts()
                .Where(account => filter.Length == 0
                    || account.UserName.Contains(filter, StringComparison.CurrentCultureIgnoreCase));

            foreach (var account in matches)
                _users.Add(new UserListRow(account, BuildSummary(account), _localization));

            UserList.SelectedItem = _users.FirstOrDefault(row =>
                string.Equals(row.UserName, selectUserName, StringComparison.OrdinalIgnoreCase))
                ?? _users.FirstOrDefault();
        }
        finally
        {
            _isReloading = false;
        }

        // Called explicitly rather than left to SelectionChanged: when the reload lands on the same
        // account that was already selected, WPF raises nothing at all and the pane would keep
        // showing pre-save values.
        ShowSelectedUser();
    }

    private string BuildSummary(UserAccount account)
    {
        if (account.IsAdministrator)
            return _localization["Shop.Role.Admin"];

        var shopCount = account.Memberships.Count(membership => membership.IsActive);

        return shopCount == 0
            ? _localization["Users.NoAccess"]
            : _localization.Format("Users.ShopCount", shopCount);
    }

    // --- Detail pane ----------------------------------------------------------------------------

    private void ShowSelectedUser()
    {
        CreatePanel.Visibility = Visibility.Collapsed;

        if (UserList.SelectedItem is not UserListRow row)
        {
            DetailPanel.Visibility = Visibility.Collapsed;
            EmptySelectionText.Visibility = Visibility.Visible;
            DeleteUserButton.IsEnabled = false;
            SaveButton.IsEnabled = false;
            return;
        }

        EmptySelectionText.Visibility = Visibility.Collapsed;
        DetailPanel.Visibility = Visibility.Visible;

        DetailUserNameText.Text = row.UserName;
        DetailSummaryText.Text = row.Summary;

        // The administrator's rights are not editable data — showing an unticked matrix for an
        // account that has every right would be a lie the user could act on.
        AdminNoticePanel.Visibility = row.IsAdministrator ? Visibility.Visible : Visibility.Collapsed;
        AssignmentsPanel.Visibility = row.IsAdministrator ? Visibility.Collapsed : Visibility.Visible;

        DeleteUserButton.IsEnabled = !row.IsAdministrator;
        SaveButton.IsEnabled = true;

        ResetPasswordBox.Clear();
        ResetPasswordConfirmBox.Clear();
        StatusText.Text = string.Empty;

        BuildAssignmentRows(row.UserName);
    }

    private void BuildAssignmentRows(string userName)
    {
        _assignments.Clear();

        var account = AuthenticationService.Instance.ListAccounts()
            .FirstOrDefault(candidate =>
                string.Equals(candidate.UserName, userName, StringComparison.OrdinalIgnoreCase));

        var held = account?.Memberships ?? Array.Empty<ShopMembership>();

        foreach (var shop in _shops)
        {
            var roles = held.FirstOrDefault(membership => membership.ShopPublicId == shop.PublicId)?.Roles
                ?? new List<UserRole>();

            _assignments.Add(new ShopAssignmentRow(
                shop.PublicId,
                shop.ResolveName(_localization.CurrentLanguageCode),
                BuildShopDetails(shop),
                roles.Contains(UserRole.Manager),
                roles.Contains(UserRole.Staff)));
        }

        NoShopsText.Visibility = _shops.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private string BuildShopDetails(Shop shop)
    {
        var currency = _localization[$"CurrencyType.{shop.CurrencyType}"];

        // An archived shop still appears, so it has to say so — otherwise an assignment to a shop
        // nobody can open reads as a bug.
        var parts = shop.IsArchived
            ? new[] { currency, _localization["Users.ArchivedShop"] }
            : new[] { currency };

        return _localization.JoinFragments(parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private void OnUserSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isReloading)
            return;

        ShowSelectedUser();
    }

    private void OnSearchTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!IsLoaded)
            return;

        ReloadUsers((UserList.SelectedItem as UserListRow)?.UserName);
    }

    // --- Save / delete --------------------------------------------------------------------------

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (UserList.SelectedItem is not UserListRow row)
            return;

        if (!TryApplyPasswordChange(row.UserName))
            return;

        if (!row.IsAdministrator)
        {
            var result = AuthenticationService.Instance.SetShopRoles(row.UserName, CollectRoles());
            if (result != AccountOperationResult.Success)
            {
                ShowError(ErrorKey(result));
                return;
            }
        }

        // Reloaded before the message, because the reload resets the status line.
        ReloadUsers(row.UserName);
        ShowStatus("Users.Saved", row.UserName);
    }

    /// <summary>
    /// Applies a password change when one was typed. Both boxes empty means "leave it alone", which
    /// is the normal case — this screen is opened to change roles far more often than passwords.
    /// </summary>
    private bool TryApplyPasswordChange(string userName)
    {
        var password = ResetPasswordBox.Password;
        var confirmation = ResetPasswordConfirmBox.Password;

        if (password.Length == 0 && confirmation.Length == 0)
            return true;

        if (!string.Equals(password, confirmation, StringComparison.Ordinal))
        {
            ShowError("Users.Error.PasswordMismatch");
            return false;
        }

        var result = AuthenticationService.Instance.SetPassword(userName, password);
        if (result == AccountOperationResult.Success)
            return true;

        ShowError(ErrorKey(result));
        return false;
    }

    /// <summary>
    /// The ticked matrix as a role set per shop. Both boxes ticked yields both roles, which is
    /// exactly how "manager and staff in the same branch" is stored. An empty set means the account
    /// is not a member of that shop at all.
    /// </summary>
    /// <remarks>
    /// Only the ROLES are sent: activation, join date and shift belong to the shop's own roster
    /// screen, and this window must not silently reset them while editing something else.
    /// </remarks>
    private Dictionary<Guid, IReadOnlyList<UserRole>> CollectRoles()
    {
        var rolesByShop = new Dictionary<Guid, IReadOnlyList<UserRole>>();

        foreach (var row in _assignments)
        {
            var roles = new List<UserRole>();

            if (row.IsManager)
                roles.Add(UserRole.Manager);

            if (row.IsStaff)
                roles.Add(UserRole.Staff);

            rolesByShop[row.ShopPublicId] = roles;
        }

        return rolesByShop;
    }

    private void OnDeleteUserClick(object sender, RoutedEventArgs e)
    {
        if (UserList.SelectedItem is not UserListRow row)
            return;

        var answer = MessageBox.Show(
            this,
            _localization.Format("Users.DeleteConfirm", row.UserName),
            _localization["Users.Delete"],
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
            return;

        var result = AuthenticationService.Instance.DeleteAccount(row.UserName);
        if (result != AccountOperationResult.Success)
        {
            ShowError(ErrorKey(result));
            return;
        }

        ReloadUsers(selectUserName: null);
        ShowStatus("Users.Deleted", row.UserName);
    }

    // --- Create ---------------------------------------------------------------------------------

    private void OnAddUserClick(object sender, RoutedEventArgs e)
    {
        NewUserNameBox.Clear();
        NewPasswordBox.Clear();
        NewPasswordConfirmBox.Clear();
        CreateErrorText.Visibility = Visibility.Collapsed;
        StatusText.Text = string.Empty;

        DetailPanel.Visibility = Visibility.Collapsed;
        EmptySelectionText.Visibility = Visibility.Collapsed;
        CreatePanel.Visibility = Visibility.Visible;

        // The footer still points at whoever was selected before this form opened, so leaving those
        // buttons live would let 保存修改 or 删除用户 act on them. Re-enabled by ShowSelectedUser.
        SaveButton.IsEnabled = false;
        DeleteUserButton.IsEnabled = false;

        NewUserNameBox.Focus();
    }

    private void OnCreateConfirmClick(object sender, RoutedEventArgs e)
    {
        if (!string.Equals(NewPasswordBox.Password, NewPasswordConfirmBox.Password, StringComparison.Ordinal))
        {
            ShowCreateError("Users.Error.PasswordMismatch");
            return;
        }

        var userName = NewUserNameBox.Text.Trim();
        var result = AuthenticationService.Instance.CreateAccount(userName, NewPasswordBox.Password);

        if (result != AccountOperationResult.Success)
        {
            ShowCreateError(ErrorKey(result));
            return;
        }

        // Lands on the new account with an empty matrix, which is the next thing to fill in.
        ReloadUsers(userName);
        ShowStatus("Users.Created", userName);
    }

    private void OnCreateCancelClick(object sender, RoutedEventArgs e) => ShowSelectedUser();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    // --- Messages -------------------------------------------------------------------------------

    private static string ErrorKey(AccountOperationResult result) => result switch
    {
        AccountOperationResult.UserNameRequired => "Users.Error.NameRequired",
        AccountOperationResult.UserNameTaken => "Users.Error.NameTaken",
        AccountOperationResult.PasswordRequired => "Users.Error.PasswordRequired",
        AccountOperationResult.NotFound => "Users.Error.NotFound",
        _ => "Users.Error.Protected"
    };

    // These take a KEY rather than a finished string on purpose. Resolving the text here keeps the
    // call sites free of repeated lookups, and — the practical reason — it means each method reads
    // instance state, so SonarLint stops asking for the `static` that a method touching only x:Name
    // controls appears to deserve but which would not compile.
    private void ShowStatus(string key, params object[] args)
    {
        StatusText.Foreground = SuccessBrush;
        StatusText.Text = _localization.Format(key, args);
    }

    private void ShowError(string key)
    {
        StatusText.Foreground = ErrorBrush;
        StatusText.Text = _localization[key];
    }

    private void ShowCreateError(string key)
    {
        CreateErrorText.Text = _localization[key];
        CreateErrorText.Visibility = Visibility.Visible;
    }

    private static readonly Brush SuccessBrush = CreateFrozenBrush("#047857");
    private static readonly Brush ErrorBrush = CreateFrozenBrush("#B91C1C");

    private static Brush CreateFrozenBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// One account as the list renders it.
/// </summary>
/// <remarks>
/// Top-level and internal rather than a private nested type for the reason documented on
/// <see cref="ShopPickerRow"/>: every member is reached only through <c>{Binding}</c>, which static
/// analysis cannot see, so private members here would each read as dead code.
/// </remarks>
internal sealed class UserListRow
{
    public UserListRow(UserAccount account, string summary, LocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(localization);

        UserName = account.UserName;
        IsAdministrator = account.IsAdministrator;
        Summary = summary;
        Initial = UserPresentation.Initial(account.UserName);
        AvatarBrush = UserPresentation.AvatarBrush(account.UserName);
        AdminBadgeText = localization["Users.AccountLocked"];
        AdminBadgeVisibility = account.IsAdministrator ? Visibility.Visible : Visibility.Collapsed;
    }

    public string UserName { get; }

    public bool IsAdministrator { get; }

    public string Summary { get; }

    public string Initial { get; }

    public Brush AvatarBrush { get; }

    public string AdminBadgeText { get; }

    public Visibility AdminBadgeVisibility { get; }
}

/// <summary>
/// One row of the shop × role matrix. The two flags are written back by the checkbox bindings, so
/// this is the only mutable presentation type here.
/// </summary>
internal sealed class ShopAssignmentRow
{
    public ShopAssignmentRow(Guid shopPublicId, string shopName, string shopDetails, bool isManager, bool isStaff)
    {
        ShopPublicId = shopPublicId;
        ShopName = shopName;
        ShopDetails = shopDetails;
        IsManager = isManager;
        IsStaff = isStaff;
    }

    public Guid ShopPublicId { get; }

    public string ShopName { get; }

    public string ShopDetails { get; }

    public bool IsManager { get; set; }

    public bool IsStaff { get; set; }
}
