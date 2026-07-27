using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CameywareOrder.Localization;
using CameywareOrder.Models;
using CameywareOrder.Services;

namespace CameywareOrder.Views;

/// <summary>
/// The open shop's roster: who works here, in what role, on what shift, and whether they are still
/// active. Opened from the main toolbar by a manager or an administrator.
///
/// Everything it edits is scoped to ONE shop — deactivating someone here does not touch the branch
/// they also work at, which is the whole reason activation lives on the membership rather than on
/// the account. Deleting the account outright DOES reach every shop, so it is administrator-only.
///
/// Needs no database: members come from <see cref="AuthenticationService"/>, and the only thing it
/// wants from the shop is its identity and name.
/// </summary>
public partial class StoreMembersWindow : Window
{
    /// <summary>
    /// Granularity of the shift pickers. Fifteen minutes covers the shifts a shop actually runs
    /// without turning the list into 1,440 entries.
    /// </summary>
    private const int ShiftStepMinutes = 15;

    private readonly LocalizationService _localization;
    private readonly Shop _shop;
    private readonly ObservableCollection<MemberRow> _members = new();
    private readonly List<TimeOption> _timeOptions;

    // Selecting an item programmatically raises SelectionChanged, and clearing the list raises it
    // with a null selection — both would repaint the detail pane mid-rebuild.
    private bool _isReloading;

    public StoreMembersWindow(LocalizationService localization, Shop shop)
    {
        InitializeComponent();

        _localization = localization;
        _shop = shop ?? throw new ArgumentNullException(nameof(shop));

        _timeOptions = BuildTimeOptions(_localization["Members.NoTime"]);

        MemberList.ItemsSource = _members;

        foreach (var box in new[] { ShiftStartBox, ShiftEndBox, NewShiftStartBox, NewShiftEndBox })
            box.ItemsSource = _timeOptions;

        ShopNameText.Text = _shop.ResolveName(_localization.CurrentLanguageCode);
        DeleteAccountButton.Visibility = AuthenticationService.Instance.CanDeleteAccounts
            ? Visibility.Visible
            : Visibility.Collapsed;

        Reload(selectUserName: null);
    }

    /// <summary>
    /// Times of day the shift pickers offer, led by a "not set" entry — a shop that does not roster
    /// shifts should not be forced to invent one.
    /// </summary>
    private static List<TimeOption> BuildTimeOptions(string emptyLabel)
    {
        var options = new List<TimeOption> { new(null, emptyLabel) };

        for (var minutes = 0; minutes < 24 * 60; minutes += ShiftStepMinutes)
        {
            var time = new TimeOnly(minutes / 60, minutes % 60);
            options.Add(new TimeOption(time, time.ToString("HH:mm", CultureInfo.InvariantCulture)));
        }

        return options;
    }

    // --- Roster -----------------------------------------------------------------------------

    private void Reload(string? selectUserName)
    {
        var members = AuthenticationService.Instance.ListMembers(_shop.PublicId);

        _isReloading = true;
        try
        {
            var filter = SearchBox.Text.Trim();

            _members.Clear();

            var matches = members.Where(member => filter.Length == 0
                || member.DisplayLabel.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
                || member.UserName.Contains(filter, StringComparison.CurrentCultureIgnoreCase));

            foreach (var member in matches)
                _members.Add(new MemberRow(member, BuildRowDetail(member), _localization));

            MemberList.SelectedItem = _members.FirstOrDefault(row =>
                string.Equals(row.UserName, selectUserName, StringComparison.OrdinalIgnoreCase))
                ?? _members.FirstOrDefault();
        }
        finally
        {
            _isReloading = false;
        }

        // Counted from the WHOLE roster, not the filtered view: "how many work here" must not change
        // because somebody typed in the search box.
        TotalCountText.Text = members.Count.ToString(CultureInfo.CurrentCulture);
        ActiveCountText.Text = members.Count(member => member.Membership.IsActive)
            .ToString(CultureInfo.CurrentCulture);
        InactiveCountText.Text = members.Count(member => !member.Membership.IsActive)
            .ToString(CultureInfo.CurrentCulture);

        // Called explicitly rather than left to SelectionChanged: when the reload lands on the same
        // member that was already selected, WPF raises nothing and the pane would keep pre-save values.
        ShowSelectedMember();
    }

    /// <summary>The second line of a roster row: role, then shift when one is set.</summary>
    private string BuildRowDetail(StoreMember member)
    {
        var parts = new List<string> { RoleSummary(member.Membership.Roles) };

        var shift = FormatShift(member.Membership);
        if (shift.Length > 0)
            parts.Add(shift);

        return string.Join("  ·  ", parts);
    }

    private string RoleSummary(IReadOnlyList<UserRole> roles)
    {
        if (roles.Count == 0)
            return _localization["Shop.Role.None"];

        return string.Join(" + ", roles.Select(role => UserPresentation.RoleText(_localization, role)));
    }

    private static string FormatShift(ShopMembership membership)
    {
        if (membership.ShiftStart is not { } start || membership.ShiftEnd is not { } end)
            return string.Empty;

        return $"{start:HH\\:mm}–{end:HH\\:mm}";
    }

    private void OnMemberSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isReloading)
            return;

        ShowSelectedMember();
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded)
            return;

        Reload((MemberList.SelectedItem as MemberRow)?.UserName);
    }

    // --- Detail pane ------------------------------------------------------------------------

    private void ShowSelectedMember()
    {
        CreatePanel.Visibility = Visibility.Collapsed;

        if (MemberList.SelectedItem is not MemberRow row)
        {
            DetailPanel.Visibility = Visibility.Collapsed;
            EmptySelectionText.Visibility = Visibility.Visible;
            SaveButton.IsEnabled = false;
            DeleteAccountButton.IsEnabled = false;
            return;
        }

        EmptySelectionText.Visibility = Visibility.Collapsed;
        DetailPanel.Visibility = Visibility.Visible;

        var member = row.Member;

        DetailNameText.Text = member.DisplayLabel;
        DetailSubtitleText.Text = _localization.Format("Members.AccountLine", member.UserName);

        // The administrator is nobody's employee: they appear in no roster, but if one ever surfaced
        // here it must not be editable from a single shop.
        AdminNoticePanel.Visibility = member.IsAdministrator ? Visibility.Visible : Visibility.Collapsed;
        EditorPanels.Visibility = member.IsAdministrator ? Visibility.Collapsed : Visibility.Visible;
        SaveButton.IsEnabled = !member.IsAdministrator;
        DeleteAccountButton.IsEnabled = !member.IsAdministrator;

        DisplayNameBox.Text = member.DisplayName ?? string.Empty;
        BirthDatePicker.SelectedDate = member.BirthDate;

        ManagerCheck.IsChecked = member.Membership.Roles.Contains(UserRole.Manager);
        StaffCheck.IsChecked = member.Membership.Roles.Contains(UserRole.Staff);
        ActiveCheck.IsChecked = member.Membership.IsActive;
        JoinedDatePicker.SelectedDate = member.Membership.JoinedOn;

        SelectTime(ShiftStartBox, member.Membership.ShiftStart);
        SelectTime(ShiftEndBox, member.Membership.ShiftEnd);

        UpdateDeactivatedDisplay(member.Membership);

        // A manager may only reset a password when the account works exclusively in shops they run.
        PasswordPanel.Visibility = AuthenticationService.Instance.CanSetPasswordFor(member.UserName)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ResetPasswordBox.Clear();
        ResetPasswordConfirmBox.Clear();

        StatusText.Text = string.Empty;
    }

    private void UpdateDeactivatedDisplay(ShopMembership membership)
    {
        var show = !membership.IsActive && membership.DeactivatedOn is not null;

        DeactivatedPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        DeactivatedOnText.Text = membership.DeactivatedOn?.ToString("g", CultureInfo.CurrentCulture)
            ?? string.Empty;
    }

    private void OnActiveChanged(object sender, RoutedEventArgs e)
    {
        // Only the ALREADY-STORED deactivation date is shown. Un-ticking the box here has not been
        // saved yet, so inventing a timestamp now would show a delisting that has not happened.
        if (MemberList.SelectedItem is MemberRow row)
            UpdateDeactivatedDisplay(row.Member.Membership);
    }

    private void SelectTime(ComboBox box, TimeOnly? value)
        => box.SelectedItem = _timeOptions.FirstOrDefault(option => option.Value == value) ?? _timeOptions[0];

    private static TimeOnly? ReadTime(ComboBox box) => (box.SelectedItem as TimeOption)?.Value;

    private static List<UserRole> ReadRoles(CheckBox managerCheck, CheckBox staffCheck)
    {
        var roles = new List<UserRole>();

        if (managerCheck.IsChecked.GetValueOrDefault())
            roles.Add(UserRole.Manager);

        if (staffCheck.IsChecked.GetValueOrDefault())
            roles.Add(UserRole.Staff);

        return roles;
    }

    // --- Save / delete ----------------------------------------------------------------------

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (MemberList.SelectedItem is not MemberRow row || row.Member.IsAdministrator)
            return;

        if (!TryApplyPasswordChange(row.UserName))
            return;

        var profile = new MemberProfile(
            DisplayNameBox.Text,
            BirthDatePicker.SelectedDate,
            ReadRoles(ManagerCheck, StaffCheck),
            ActiveCheck.IsChecked.GetValueOrDefault(),
            JoinedDatePicker.SelectedDate,
            ReadTime(ShiftStartBox),
            ReadTime(ShiftEndBox));

        var result = AuthenticationService.Instance.UpdateMember(_shop.PublicId, row.UserName, profile);

        if (result != AccountOperationResult.Success)
        {
            ShowError(ErrorKey(result));
            return;
        }

        // Reloaded before the message, because the reload resets the status line.
        Reload(row.UserName);
        ShowStatus("Members.Saved", row.Member.DisplayLabel);
    }

    private bool TryApplyPasswordChange(string userName)
    {
        var password = ResetPasswordBox.Password;
        var confirmation = ResetPasswordConfirmBox.Password;

        if (password.Length == 0 && confirmation.Length == 0)
            return true;

        if (!AuthenticationService.Instance.CanSetPasswordFor(userName))
        {
            ShowError("Users.Error.Protected");
            return false;
        }

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

    private void OnDeleteAccountClick(object sender, RoutedEventArgs e)
    {
        if (MemberList.SelectedItem is not MemberRow row)
            return;

        // Defence in depth: the button is hidden for a manager, but the check belongs where the
        // action happens too.
        if (!AuthenticationService.Instance.CanDeleteAccounts)
            return;

        var answer = MessageBox.Show(
            this,
            _localization.Format("Members.DeleteConfirm", row.Member.DisplayLabel),
            _localization["Members.DeleteAccount"],
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

        Reload(selectUserName: null);
        ShowStatus("Members.Deleted", row.Member.DisplayLabel);
    }

    // --- Add a member -----------------------------------------------------------------------

    private void OnAddMemberClick(object sender, RoutedEventArgs e)
    {
        NewUserNameBox.Clear();
        NewDisplayNameBox.Clear();
        NewPasswordBox.Clear();
        NewPasswordConfirmBox.Clear();
        NewBirthDatePicker.SelectedDate = null;
        NewJoinedDatePicker.SelectedDate = DateTime.Today;
        NewManagerCheck.IsChecked = false;
        NewStaffCheck.IsChecked = true;
        SelectTime(NewShiftStartBox, null);
        SelectTime(NewShiftEndBox, null);
        CreateErrorText.Visibility = Visibility.Collapsed;
        StatusText.Text = string.Empty;

        DetailPanel.Visibility = Visibility.Collapsed;
        EmptySelectionText.Visibility = Visibility.Collapsed;
        CreatePanel.Visibility = Visibility.Visible;

        // The footer still points at whoever was selected before this form opened, so leaving those
        // buttons live would let 保存修改 write the new form's absence over an existing member — or
        // 删除账户 delete them. Re-enabled by ShowSelectedMember when the form closes.
        SaveButton.IsEnabled = false;
        DeleteAccountButton.IsEnabled = false;

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

        var profile = new MemberProfile(
            NewDisplayNameBox.Text,
            NewBirthDatePicker.SelectedDate,
            ReadRoles(NewManagerCheck, NewStaffCheck),
            IsActive: true,
            NewJoinedDatePicker.SelectedDate,
            ReadTime(NewShiftStartBox),
            ReadTime(NewShiftEndBox));

        var result = AuthenticationService.Instance.AddMember(
            _shop.PublicId, userName, NewPasswordBox.Password, profile);

        if (result != AccountOperationResult.Success)
        {
            ShowCreateError(ErrorKey(result));
            return;
        }

        Reload(userName);
        ShowStatus("Members.Created", string.IsNullOrWhiteSpace(NewDisplayNameBox.Text)
            ? userName
            : NewDisplayNameBox.Text.Trim());
    }

    private void OnCreateCancelClick(object sender, RoutedEventArgs e) => ShowSelectedMember();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    // --- Messages ---------------------------------------------------------------------------

    private static string ErrorKey(AccountOperationResult result) => result switch
    {
        AccountOperationResult.UserNameRequired => "Users.Error.NameRequired",
        AccountOperationResult.UserNameTaken => "Users.Error.NameTaken",
        AccountOperationResult.PasswordRequired => "Users.Error.PasswordRequired",
        AccountOperationResult.RoleRequired => "Members.Error.RoleRequired",
        AccountOperationResult.NotFound => "Users.Error.NotFound",
        _ => "Members.Error.Protected"
    };

    // These take a KEY rather than a finished string on purpose — see the same pattern in
    // UserManagementWindow: it keeps the call sites free of repeated lookups and means each method
    // reads instance state, so SonarLint stops asking for a `static` that would not compile.
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

/// <summary>One selectable time in the shift pickers; <c>null</c> is the "not set" entry.</summary>
internal sealed record TimeOption(TimeOnly? Value, string Text);

/// <summary>
/// One roster row.
/// </summary>
/// <remarks>
/// Top-level and internal rather than a private nested type for the reason documented on
/// <see cref="ShopPickerRow"/>: every member is reached only through <c>{Binding}</c>, which static
/// analysis cannot see, so private members here would each read as dead code.
/// </remarks>
internal sealed class MemberRow
{
    private static readonly Brush ActiveBackground = Frozen("#D1FAE5");
    private static readonly Brush ActiveForeground = Frozen("#065F46");
    private static readonly Brush InactiveBackground = Frozen("#FEE2E2");
    private static readonly Brush InactiveForeground = Frozen("#991B1B");

    public MemberRow(StoreMember member, string detail, LocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(localization);

        Member = member;
        Name = member.DisplayLabel;
        Detail = detail;
        Initial = UserPresentation.Initial(member.DisplayLabel);
        AvatarBrush = UserPresentation.AvatarBrush(member.UserName);

        var active = member.Membership.IsActive;
        StatusText = localization[active ? "Members.Active" : "Members.Deactivated"];
        StatusBackground = active ? ActiveBackground : InactiveBackground;
        StatusForeground = active ? ActiveForeground : InactiveForeground;

        // A delisted member stays in the list but reads as past tense.
        RowOpacity = active ? 1.0 : 0.55;
    }

    public StoreMember Member { get; }

    public string UserName => Member.UserName;

    public string Name { get; }

    public string Detail { get; }

    public string Initial { get; }

    public Brush AvatarBrush { get; }

    public string StatusText { get; }

    public Brush StatusBackground { get; }

    public Brush StatusForeground { get; }

    public double RowOpacity { get; }

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
