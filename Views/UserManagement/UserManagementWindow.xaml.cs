using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
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
/// picker and from Local Configuration → User Management.
///
/// Accounts come from <see cref="AuthenticationService"/> (a file outside the database) while the
/// shops they are assigned to come from the database, so this window needs both. It edits ONE
/// account at a time and writes on Save Changes: an assignment matrix that saved on every tick would
/// revoke access halfway through a re-assignment.
/// </summary>
public partial class UserManagementWindow : Window
{
    private readonly LocalizationService _localization;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ObservableCollection<UserListRow> _users = new();
    private readonly ObservableCollection<ShopAssignmentRow> _assignments = new();
    private readonly List<Shop> _shops;

    // The same set as _shops, as a lookup: BuildSummary asks it once per account row.
    private readonly HashSet<Guid> _existingShopIds;

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
        _existingShopIds = _shops.Select(shop => shop.PublicId).ToHashSet();

        ReloadUsers(selectUserName: null);
    }

    /// <summary>
    /// The account the administrator asked to sign in as, or null when they did not.
    /// </summary>
    /// <remarks>
    /// Reported rather than acted on. Swapping the session from inside a dialog would pull this
    /// window's own ground out from under it — and the caller has to take the MAIN window down and
    /// re-run the shop picker anyway, neither of which is this screen's job.
    /// </remarks>
    public string? SignInAsUserName { get; private set; }

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

            // Matched on the NAME as well as the login, because the list now shows the name — a
            // search that ignores what is on screen reads as broken.
            var matches = AuthenticationService.Instance.ListAccounts()
                .Where(account => filter.Length == 0
                    || account.UserName.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
                    || account.FullName.Contains(filter, StringComparison.CurrentCultureIgnoreCase));

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

    /// <summary>
    /// "Three shops" under an account's name — counted against the shops that EXIST.
    /// </summary>
    /// <remarks>
    /// The membership list alone is not the answer, for the same reason it is not the authority on
    /// which ROLES exist: it is a set of references, and a reference outlives what it names. A shop
    /// deleted before v9.5.1 left its memberships in `credentials.json`, and this line counted them —
    /// an installation with one shop reported three, above a matrix showing the one. Deleting now
    /// withdraws them (`ShopAdministration.Delete`), but that repairs nothing already written, and it
    /// is not the only way the two can part company: `credentials.json` lives outside the database
    /// and whole databases move between machines.
    ///
    /// So this counts what <see cref="BuildAssignmentRows"/> can draw, which is the number the person
    /// reading it is about to check against the list — and it is right on a file written by any
    /// version.
    /// </remarks>
    private string BuildSummary(UserAccount account)
    {
        if (account.IsAdministrator)
            return _localization["Shop.Role.Admin"];

        var shopCount = account.Memberships.Count(membership =>
            membership.IsActive && _existingShopIds.Contains(membership.ShopPublicId));

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

        DetailNameText.Text = row.DisplayLabel;
        DetailLoginText.Text = _localization.Format("Members.AccountLine", row.UserName);
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

        // The profile is account-level, so it loads for the administrator too — unlike the
        // assignment matrix above, filling it in grants nobody anything.
        var selected = FindAccount(row.UserName);
        FirstNameBox.Text = selected?.FirstName ?? string.Empty;
        LastNameBox.Text = selected?.LastName ?? string.Empty;
        LoginBox.Text = row.UserName;
        ContactPhoneField.Load(selected?.PhoneNumber, ShopContext.Instance.Current);
        ContactEmailBox.Text = selected?.Email ?? string.Empty;
        ContactErrorText.Visibility = Visibility.Collapsed;

        LoginErrorText.Visibility = Visibility.Collapsed;

        // Signing in as YOURSELF does nothing, and signing in as an account every shop has delisted
        // would land on "no shop is available" and then back at the login screen — having spent the
        // administrator's own session to learn what the roster already shows. Hidden rather than
        // disabled, per the convention on every other gated control here.
        SignInAsButton.Visibility = CanSignInAs(selected) ? Visibility.Visible : Visibility.Collapsed;

        // The administrator's login cannot be changed. DISABLED rather than read-only: a read-only
        // box looks exactly like an editable one and simply swallows the typing, which reads as the
        // application being broken — the report that prompted this said "the system blocked me" with
        // no idea why. Greyed out, with the reason under it, the state is legible before anybody
        // tries.
        LoginBox.IsEnabled = !row.IsAdministrator;
        LoginHintText.Text = _localization[row.IsAdministrator ? "Users.LoginLocked" : "Users.LoginHint"];

        BuildAssignmentRows(row.UserName);
    }

    /// <summary>
    /// Reports whether the login being typed is already somebody else's, at the keystroke.
    /// </summary>
    /// <remarks>
    /// The save path re-checks and refuses regardless — this is the courtesy, not the guard. The
    /// account being edited is excluded from the comparison, or every account would report its own
    /// name as taken the moment the box was touched.
    /// </remarks>
    private void OnLoginTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (UserList.SelectedItem is not UserListRow row)
            return;

        if (AuthenticationService.Instance.IsUserNameTakenByAnother(LoginBox.Text, row.UserName))
            ShowLoginError("Users.Error.NameTaken");
        else
            LoginErrorText.Visibility = Visibility.Collapsed;
    }

    /// <summary>The same answer on the create form, where nothing is being edited to exclude.</summary>
    private void OnNewUserNameTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var taken = AuthenticationService.Instance.IsUserNameTaken(NewUserNameBox.Text);
        NewUserNameTakenText.Visibility = taken ? Visibility.Visible : Visibility.Collapsed;
    }

    private static UserAccount? FindAccount(string userName)
        => AuthenticationService.Instance.ListAccounts()
            .FirstOrDefault(candidate =>
                string.Equals(candidate.UserName, userName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether "sign in as this user" is worth offering for an account.
    /// </summary>
    /// <remarks>
    /// The same three conditions the service enforces, asked here so the button is simply absent
    /// rather than present and refusing. The service still enforces them — a hidden button is a fact
    /// about the UI, not a permission.
    /// </remarks>
    private static bool CanSignInAs(UserAccount? account)
    {
        var auth = AuthenticationService.Instance;

        if (account is null || !auth.CanManageUsers)
            return false;

        if (string.Equals(account.UserName, auth.CurrentUser?.UserName, StringComparison.OrdinalIgnoreCase))
            return false;

        // Delisted everywhere: the same accounts sign-in itself refuses.
        return account.Memberships.Count == 0
               || account.Memberships.Any(membership => membership.IsActive);
    }

    /// <summary>
    /// Hands the session to the selected account. Confirms first, then closes: the caller performs
    /// the switch, because it owns the main window that has to come down with it.
    /// </summary>
    private void OnSignInAsClick(object sender, RoutedEventArgs e)
    {
        if (UserList.SelectedItem is not UserListRow row)
            return;

        // Defence in depth: the button is hidden otherwise, but the check belongs where the action
        // happens too.
        if (!CanSignInAs(FindAccount(row.UserName)))
            return;

        // Consequential and not obviously reversible — the administrator's own session ends here and
        // comes back only by signing in again — so it asks, the way deleting an account does.
        var answer = MessageBox.Show(
            this,
            _localization.Format("Users.SignInAsConfirm", row.DisplayLabel, row.UserName),
            _localization["Users.SignInAs"],
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
            return;

        SignInAsUserName = row.UserName;
        Close();
    }

    /// <summary>
    /// Asks before changing what somebody signs in with. Returns true when the save may proceed.
    /// Only ever reached for a login that IS changing and IS available.
    /// </summary>
    /// <remarks>
    /// A rename sits behind the same button as a phone-number edit, so it is easy to trigger by
    /// accident — and its consequence lands on somebody else, at their next sign-in, with nothing on
    /// their screen to explain it.
    ///
    /// Not reachable from an automated check, and deliberately left that way. A MessageBox is a
    /// native modal that cannot be dismissed from inside the process, and the seams that would let a
    /// harness answer it — a virtual method, an injectable delegate — are either impossible
    /// (subclassing a XAML window breaks <c>InitializeComponent</c>, which resolves its resource by
    /// exact type) or a test hook in shipping code. `namecheck` therefore drives this save path for
    /// everything EXCEPT a confirmed rename, and covers the rename itself against the service.
    /// </remarks>
    private bool ConfirmRename(string currentUserName, string newUserName)
        => MessageBox.Show(
            this,
            _localization.Format("Users.RenameConfirm", currentUserName, newUserName),
            _localization["Users.Save"],
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;

    private void ShowLoginError(string key)
    {
        LoginErrorText.Text = _localization[key];
        LoginErrorText.Visibility = Visibility.Visible;

        // A stale "changes were saved" sitting under a failed save is worse than no message at all.
        StatusText.Text = string.Empty;
    }

    private void ShowContactError(string key)
    {
        ContactErrorText.Text = _localization[key];
        ContactErrorText.Visibility = Visibility.Visible;
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
            var roleIds = held
                .FirstOrDefault(membership => membership.ShopPublicId == shop.PublicId)?.RoleIds;

            _assignments.Add(new ShopAssignmentRow(
                shop.PublicId,
                shop.ResolveName(_localization.CurrentLanguageCode),
                BuildShopDetails(shop),
                RoleToggle.ForMembership(_localization, roleIds)));
        }

        NoShopsText.Visibility = _shops.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private string BuildShopDetails(Shop shop)
    {
        var currency = _localization.JoinList(
            ShopCurrencies.Supported(shop).Select(item => ShopCurrencies.Name(item, _localization)));

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

    /// <summary>
    /// Saves everything on the pane: the person's name and login, their contact details, a password
    /// if one was typed, and the shop × role matrix.
    /// </summary>
    /// <remarks>
    /// ONE Save for the whole screen. The profile card used to carry a second button with the same
    /// label, and this one saved only the password and the roles — so editing a name or a login and
    /// pressing the obvious button in the footer discarded the edit on the reload that followed,
    /// with a "changes were saved" message on top of it. Two buttons that say Save and mean
    /// different subsets is not a thing to explain in a tooltip.
    ///
    /// The profile goes FIRST because it may rename the account, and everything after it has to act
    /// on the new login.
    /// </remarks>
    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (UserList.SelectedItem is not UserListRow row)
            return;

        if (!TryApplyProfileChange(row, out var userName))
            return;

        if (!TryApplyPasswordChange(userName))
            return;

        if (!row.IsAdministrator)
        {
            var result = AuthenticationService.Instance.SetShopRoles(userName, CollectRoles());
            if (result != AccountOperationResult.Success)
            {
                ShowError(ErrorKey(result));
                return;
            }
        }

        // Reloaded before the message, because the reload resets the status line. Selecting the NEW
        // login: after a rename the old one matches nothing and the list would fall to its first row.
        ReloadUsers(userName);
        ShowStatus("Users.Saved", userName);
    }

    /// <summary>
    /// Applies the profile card. <paramref name="userName"/> comes back as the login the account has
    /// AFTER the call, which is what the rest of the save must use.
    /// </summary>
    private bool TryApplyProfileChange(UserListRow row, out string userName)
    {
        userName = row.UserName;

        // The same rules the order form applies to a customer's details, through the same property.
        // A STORED number keeps the loose rule — see StoreMembersWindow for why editing a record
        // saved before the country rule must not be blocked by it — while a number typed here is
        // held to the country's own, because it is being typed now rather than re-read from a record.
        if (!ContactPhoneField.IsAcceptable)
        {
            ShowContactError("OrderEdit.Validate.PhoneInvalid");
            return false;
        }

        if (!ContactValidation.IsValidEmail(ContactEmailBox.Text))
        {
            ShowContactError("OrderEdit.Validate.EmailInvalid");
            return false;
        }

        // A disabled box still reports its text, so the administrator's login arrives unchanged and
        // is not treated as a rename at all.
        var login = LoginBox.Text.Trim();
        var isRename = !string.Equals(login, row.UserName, StringComparison.Ordinal);

        // Availability is settled BEFORE the confirmation. Asking "are you sure you want to rename
        // this to X" and only then reporting that X is unavailable wastes the question.
        if (isRename && AuthenticationService.Instance.IsUserNameTaken(login))
        {
            ShowLoginError("Users.Error.NameTaken");
            return false;
        }

        if (isRename && !ConfirmRename(row.UserName, login))
            return false;

        var result = AuthenticationService.Instance.UpdateAccountProfile(
            row.UserName,
            new AccountProfile(login, FirstNameBox.Text, LastNameBox.Text,
                ContactPhoneField.FullNumber, ContactEmailBox.Text));

        if (result != AccountOperationResult.Success)
        {
            // Anything about the LOGIN belongs under the login box; everything else under the card.
            if (result is AccountOperationResult.UserNameTaken or AccountOperationResult.UserNameRequired)
                ShowLoginError(ErrorKey(result));
            else
                ShowContactError(ErrorKey(result));

            return false;
        }

        ContactErrorText.Visibility = Visibility.Collapsed;
        LoginErrorText.Visibility = Visibility.Collapsed;
        userName = login;
        return true;
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

        // requireChange: an administrator has just chosen and read out a password for somebody else,
        // so it is a handover credential, not their password. They replace it on first sign-in.
        var result = AuthenticationService.Instance.SetPassword(userName, password, requireChange: true);
        if (result == AccountOperationResult.Success)
            return true;

        ShowError(ErrorKey(result), AuthenticationService.MinimumPasswordLength);
        return false;
    }

    /// <summary>
    /// The ticked matrix as a role set per shop. Several boxes ticked yields several roles, which is
    /// exactly how "manager and auditor in the same branch" is stored. An empty set means the
    /// account is not a member of that shop at all.
    /// </summary>
    /// <remarks>
    /// Only the ROLES are sent: activation, join date and shift belong to the shop's own roster
    /// screen, and this window must not silently reset them while editing something else.
    /// </remarks>
    private Dictionary<Guid, IReadOnlyList<string>> CollectRoles()
    {
        var rolesByShop = new Dictionary<Guid, IReadOnlyList<string>>();

        foreach (var row in _assignments)
            rolesByShop[row.ShopPublicId] = RoleToggle.Selected(row.Roles);

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
        NewUserNameTakenText.Visibility = Visibility.Collapsed;
        StatusText.Text = string.Empty;

        DetailPanel.Visibility = Visibility.Collapsed;
        EmptySelectionText.Visibility = Visibility.Collapsed;
        CreatePanel.Visibility = Visibility.Visible;

        // The footer still points at whoever was selected before this form opened, so leaving those
        // buttons live would let Save Changes or Delete User act on them. Re-enabled by ShowSelectedUser.
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
            ShowCreateError(ErrorKey(result), AuthenticationService.MinimumPasswordLength);
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
        AccountOperationResult.PasswordTooShort => "Users.Error.PasswordTooShort",
        AccountOperationResult.PasswordSameAsUserName => "Users.Error.PasswordSameAsUserName",
        AccountOperationResult.NotFound => "Users.Error.NotFound",
        AccountOperationResult.Deactivated => "Users.Error.SignInAsDeactivated",
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

    // The password-policy messages quote the minimum length, so these take arguments like ShowStatus
    // rather than a bare lookup. Format on a message with no placeholder returns it unchanged, so
    // the call sites that have nothing to pass do not have to care.
    private void ShowError(string key, params object[] args)
    {
        StatusText.Foreground = ErrorBrush;
        StatusText.Text = _localization.Format(key, args);
    }

    private void ShowCreateError(string key, params object[] args)
    {
        CreateErrorText.Text = _localization.Format(key, args);
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
        DisplayLabel = account.DisplayLabel;
        IsAdministrator = account.IsAdministrator;
        Summary = summary;
        Label = BuildLabel(account, localization);

        // The tile's LETTER comes from the name, because that is what the row shows; its COLOUR
        // comes from the login, which is the stable identity — a person correcting the spelling of
        // their own name should not find their avatar has changed colour.
        Initial = UserPresentation.Initial(account.DisplayLabel);
        AvatarBrush = UserPresentation.AvatarBrush(account.UserName);
        AdminBadgeText = localization["Users.AccountLocked"];
        AdminBadgeVisibility = account.IsAdministrator ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// "Tina (Manager, Staff)" — who they are, and what they are. The whole shape lives in the
    /// string table rather than being concatenated here: Chinese and Japanese bracket fullwidth where
    /// English uses ( ), and building it in code produces one of them in every language. An account
    /// holding no role at all is just its name — empty brackets read as a rendering fault.
    /// </summary>
    private static string BuildLabel(UserAccount account, LocalizationService localization)
    {
        var roles = UserPresentation.RoleList(localization, account.HeldRoles());

        return roles.Length == 0
            ? account.DisplayLabel
            : localization.Format("Users.AccountLabel", account.DisplayLabel, roles);
    }

    public string UserName { get; }

    /// <summary>The person's name, or the login when they have none.</summary>
    public string DisplayLabel { get; }

    /// <summary>Name and roles as the list renders them.</summary>
    [SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed",
        Justification = "Bound as Text=\"{Binding Label}\" in the UserList item template in " +
                        "UserManagementWindow.xaml; XAML bindings are invisible to Roslyn.")]
    public string Label { get; }

    public bool IsAdministrator { get; }

    public string Summary { get; }

    public string Initial { get; }

    public Brush AvatarBrush { get; }

    public string AdminBadgeText { get; }

    public Visibility AdminBadgeVisibility { get; }
}

/// <summary>
/// One row of the shop × role matrix: a shop, and every role this account could hold in it.
/// </summary>
/// <remarks>
/// The matrix used to be two fixed columns, Manager and Staff, with a bool apiece. It is now one
/// column per row holding as many toggles as the installation has defined roles — the same shape as
/// the roster screen's picker, because they are answering the same question about the same data.
/// </remarks>
internal sealed class ShopAssignmentRow
{
    public ShopAssignmentRow(
        Guid shopPublicId, string shopName, string shopDetails, IReadOnlyList<RoleToggle> roles)
    {
        ShopPublicId = shopPublicId;
        ShopName = shopName;
        ShopDetails = shopDetails;
        Roles = roles;
    }

    public Guid ShopPublicId { get; }

    public string ShopName { get; }

    public string ShopDetails { get; }

    /// <summary>Mutable by the checkbox bindings — the tick state IS the edit.</summary>
    public IReadOnlyList<RoleToggle> Roles { get; }
}
