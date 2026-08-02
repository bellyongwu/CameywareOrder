using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CameywareOrder.Data;
using CameywareOrder.Localization;
using CameywareOrder.Models;
using CameywareOrder.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CameywareOrder.Views;

/// <summary>
/// Administrator-only screen for what the application's roles mean. Reached from the shop picker and
/// from Local Configuration → Permissions.
/// </summary>
/// <remarks>
/// THREE COLUMNS, ONE QUESTION EACH: which roles exist, where each is varied, and what it allows.
/// See the markup for why that replaced the two trees this screen used to be.
///
/// A ROLE IS A NAME THE INSTALLATION SHARES; WHAT IT ALLOWS CAN DIFFER BY BRANCH. "Manager" is the
/// same job title everywhere, but the branch that also runs the workshop may let its manager edit the
/// product catalogue where a concession counter does not. Dragging a role onto a shop gives it an
/// INSTANCE there — seeded from its default, then free to diverge. A shop with no instance uses the
/// default, which is what every installation had before instances existed.
///
/// EVERYTHING IS WRITTEN ON APPLY, never per tick. A permission panel that saved as it went would
/// revoke a manager's access halfway through being re-graded — and the administrator doing it might
/// be revoking their own. Re-resolving what the signed-in user may do is NOT done here either:
/// <c>AuthenticationService</c> subscribes to the store's own <c>RolesChanged</c>, so every write
/// through the store refreshes it once, wherever the write came from.
/// </remarks>
public partial class PermissionsWindow : Window
{
    /// <summary>How far the pointer must move before a click on a role becomes a drag.</summary>
    /// <remarks>
    /// Without a threshold every click that wobbles by a pixel starts a drag, and selecting a role in
    /// order to read it becomes impossible. The SYSTEM's value, so it matches dragging anywhere else
    /// on the machine rather than a number invented here.
    /// </remarks>
    private static readonly Size DragThreshold =
        new(SystemParameters.MinimumHorizontalDragDistance, SystemParameters.MinimumVerticalDragDistance);

    private const string RoleDragFormat = "CameywareOrder.RoleId";

    private readonly LocalizationService _localization;
    private readonly List<Shop> _shops;

    private readonly ObservableCollection<RoleRow> _roleRows = new();
    private readonly ObservableCollection<ShopRow> _shopRows = new();

    private Point _dragOrigin;
    private RoleRow? _dragCandidate;

    /// <summary>The toggles currently on screen, so Apply can read them back.</summary>
    private List<CapabilityToggle> _toggles = new();

    public PermissionsWindow(LocalizationService localization, IServiceScopeFactory scopeFactory)
    {
        InitializeComponent();

        ArgumentNullException.ThrowIfNull(scopeFactory);

        _localization = localization;

        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Delisted shops included. A branch out of service still has people and still has
            // permissions; hiding it here would make its settings unreachable without putting it back
            // into service first.
            _shops = db.Shops.AsNoTracking().OrderBy(shop => shop.Id).ToList();
        }

        RoleList.ItemsSource = _roleRows;
        ShopList.ItemsSource = _shopRows;

        LoadRoles(selectId: null);
    }

    // ── 1. the catalogue ──────────────────────────────────────────────────────────────────────────

    private void LoadRoles(string? selectId)
    {
        var store = RolePermissionStore.Instance;

        _roleRows.Clear();
        foreach (var role in store.All())
            _roleRows.Add(BuildRoleRow(role, store));

        RoleList.SelectedItem =
            _roleRows.FirstOrDefault(row => IdMatches(row.RoleId, selectId)) ?? _roleRows.FirstOrDefault();
    }

    private RoleRow BuildRoleRow(RoleDefinition role, RolePermissionStore store)
    {
        var varied = role.IsAdministratorRole ? 0 : store.ShopsWithInstance(role.Id).Count;

        // Two different facts, two sentences: how much the role grants, and whether any branch has
        // moved away from that. Joined through JoinFragments so each language punctuates its own way.
        var detail = varied == 0
            ? _localization.Format("Permission.GrantsCount", role.Capabilities.Count)
            : _localization.JoinFragments(new[]
            {
                _localization.Format("Permission.GrantsCount", role.Capabilities.Count),
                _localization.Format("Permission.VariedIn", varied),
            });

        return new RoleRow(
            role.Id,
            role.ResolveName(_localization),
            detail,
            role.IsAdministratorRole,
            role.IsBuiltIn,
            role.IsAdministratorRole ? Visibility.Visible : Visibility.Collapsed,
            _localization["Permission.Locked"]);
    }

    private RoleRow? SelectedRole => RoleList.SelectedItem as RoleRow;

    private void OnRoleSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var role = SelectedRole;

        // The administrator is regenerated rather than stored — it is defined as "every capability
        // there is" — so it can be read and nothing more.
        var editable = role is { IsAdministrator: false };

        RenameRoleButton.IsEnabled = editable;
        DeleteRoleButton.IsEnabled = editable && role is { IsBuiltIn: false };

        LoadShopsForSelectedRole(selectShop: null);
        LoadCapabilityEditor();
    }

    private void OnAddRoleClick(object sender, RoutedEventArgs e)
    {
        if (!AskForName(_localization["Permission.AddRole"], string.Empty, out var name))
            return;

        // Seeded from the staff role rather than from nothing. A role created empty grants nobody
        // anything, so the first thing anybody would do with it is tick the same dozen boxes the
        // shipped role already carries; starting from the narrowest real job is a better guess than
        // starting from a blank one.
        var seed = RolePermissionStore.Instance.Find(RoleDefinition.StaffId)?.Capabilities
                   ?? (IReadOnlyCollection<AppCapability>)Array.Empty<AppCapability>();

        var result = RolePermissionStore.Instance.Create(_localization, name, seed, out var createdId);

        if (result != RoleOperationResult.Success)
        {
            ReportFailure(Describe(result));
            return;
        }

        LoadRoles(createdId);
        ReportSuccess(_localization.Format("Permission.RoleAdded", name));
    }

    private void OnRenameRoleClick(object sender, RoutedEventArgs e)
    {
        if (SelectedRole is not { IsAdministrator: false } role)
            return;

        if (!AskForName(_localization["Permission.Rename"], role.Name, out var name))
            return;

        var result = RolePermissionStore.Instance.Rename(_localization, role.RoleId, name);

        if (result != RoleOperationResult.Success)
        {
            ReportFailure(Describe(result));
            return;
        }

        LoadRoles(role.RoleId);
        ReportSuccess(_localization.Format("Permission.RoleRenamed", name));
    }

    /// <summary>
    /// Deletes a role, after saying how many people hold it.
    /// </summary>
    /// <remarks>
    /// The holder count IS the confirmation. "Delete this role?" is a question nobody can answer
    /// safely; "delete this role, which four people hold?" is one they can. The role is then withdrawn
    /// from those memberships, so nobody is left naming a role that no longer exists.
    /// </remarks>
    private void OnDeleteRoleClick(object sender, RoutedEventArgs e)
    {
        if (SelectedRole is not { IsAdministrator: false, IsBuiltIn: false } role)
            return;

        var holders = AuthenticationService.Instance.HoldersOf(role.RoleId);

        var answer = MessageBox.Show(
            holders == 0
                ? _localization.Format("Permission.DeleteConfirm", role.Name)
                : _localization.Format("Permission.DeleteConfirmHeld", role.Name, holders),
            _localization["Permission.DeleteRole"],
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
            return;

        var result = RolePermissionStore.Instance.Delete(role.RoleId);

        if (result != RoleOperationResult.Success)
        {
            ReportFailure(Describe(result));
            return;
        }

        AuthenticationService.Instance.DropRole(role.RoleId);
        LoadRoles(selectId: null);
        ReportSuccess(_localization.Format("Permission.RoleDeleted", role.Name));
    }

    // ── 2. where it is varied ─────────────────────────────────────────────────────────────────────

    private void LoadShopsForSelectedRole(Guid? selectShop)
    {
        _shopRows.Clear();

        if (SelectedRole is { IsAdministrator: false } role)
        {
            var store = RolePermissionStore.Instance;

            foreach (var shop in _shops)
                _shopRows.Add(BuildShopRow(shop, store.HasInstance(role.RoleId, shop.PublicId)));
        }

        // Two different empty states. "Nothing selected" is not the same as "the administrator cannot
        // be varied at all", and a reader told the wrong one goes looking for a bug.
        ShopEmptyText.Text = SelectedRole switch
        {
            null => _localization["Permission.PickARole"],
            { IsAdministrator: true } => _localization["Permission.AdminNotVaried"],
            _ => string.Empty
        };

        ShopEmptyText.Visibility = _shopRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        ShopList.SelectedItem = _shopRows.FirstOrDefault(row => row.PublicId == selectShop);
        RefreshInstanceActionState();
    }

    private ShopRow BuildShopRow(Shop shop, bool hasInstance)
    {
        var name = shop.ResolveName(_localization.CurrentLanguageCode);

        return new ShopRow(
            shop.PublicId,
            name,
            _localization[hasInstance ? "Permission.UsesOwnSet" : "Permission.UsesDefault"],
            hasInstance,
            _localization[hasInstance ? "Permission.State.Varied" : "Permission.State.Default"],
            Frozen(hasInstance ? "#FEF3C7" : "#F3F4F6"),
            Frozen(hasInstance ? "#92400E" : "#6B7280"));
    }

    private ShopRow? SelectedShop => ShopList.SelectedItem as ShopRow;

    private void OnShopSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshInstanceActionState();
        LoadCapabilityEditor();
    }

    private void RefreshInstanceActionState()
        => RemoveInstanceButton.IsEnabled = SelectedShop is { HasInstance: true };

    /// <summary>Drops a shop's own set, so it goes back to using the role's default.</summary>
    private void OnRemoveInstanceClick(object sender, RoutedEventArgs e)
    {
        if (SelectedRole is not { } role || SelectedShop is not { HasInstance: true } shop)
            return;

        // Worth a question: the branch's own set is discarded, and the role's default may allow more
        // or less than it did. Which of the two it is cannot be said in one sentence, so the dialog
        // names what is being discarded instead of guessing at the consequence.
        var answer = MessageBox.Show(
            _localization.Format("Permission.RemoveInstanceConfirm", role.Name, shop.Name),
            _localization["Permission.RemoveInstance"],
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
            return;

        RolePermissionStore.Instance.RemoveInstance(role.RoleId, shop.PublicId);

        LoadRoles(role.RoleId);
        LoadShopsForSelectedRole(shop.PublicId);
        ReportSuccess(_localization.Format("Permission.InstanceRemoved", role.Name, shop.Name));
    }

    // ── dragging a role onto a shop ───────────────────────────────────────────────────────────────

    private void OnRoleDragStart(object sender, MouseButtonEventArgs e)
    {
        _dragOrigin = e.GetPosition(null);

        // The row UNDER THE POINTER, not the selected one: the press that starts a drag is also the
        // press that changes the selection, and reading the selection here would drag whatever was
        // selected before this click.
        _dragCandidate = e.OriginalSource is DependencyObject source ? FindRoleRow(source) : null;
    }

    private void OnRoleDragMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragCandidate is null)
            return;

        var moved = e.GetPosition(null) - _dragOrigin;

        if (Math.Abs(moved.X) < DragThreshold.Width && Math.Abs(moved.Y) < DragThreshold.Height)
            return;

        var role = _dragCandidate;
        _dragCandidate = null;

        // The administrator cannot be varied, so it must not behave as though it can — an affordance
        // that leads only to a refusal is worse than no affordance.
        if (role.IsAdministrator)
            return;

        DragDrop.DoDragDrop(RoleList, new DataObject(RoleDragFormat, role.RoleId), DragDropEffects.Copy);
    }

    private void OnShopDragOver(object sender, DragEventArgs e)
    {
        var accepted = e.Data.GetDataPresent(RoleDragFormat);

        e.Effects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;

        // The target says so itself. A drop zone that looks identical whether or not it will accept
        // what is being carried is one people drop onto twice and then give up on.
        ShopDropTarget.BorderBrush = (Brush)FindResource(accepted ? "PrimaryBrush" : "BorderBrush");
    }

    private void OnShopDragLeave(object sender, DragEventArgs e)
        => ShopDropTarget.BorderBrush = (Brush)FindResource("BorderBrush");

    private void OnShopDrop(object sender, DragEventArgs e)
    {
        ShopDropTarget.BorderBrush = (Brush)FindResource("BorderBrush");

        if (e.Data.GetData(RoleDragFormat) is not string roleId)
            return;

        // The shop under the pointer. A drop on the blank space below the last row lands on nothing
        // and is reported rather than guessed at — guessing would vary a branch nobody pointed at.
        var target = e.OriginalSource is DependencyObject source ? FindShopRow(source) : null;

        if (target is null)
        {
            ReportFailure(_localization["Permission.DropOnAShop"]);
            return;
        }

        var roleName = _roleRows.FirstOrDefault(row => IdMatches(row.RoleId, roleId))?.Name ?? roleId;

        if (RolePermissionStore.Instance.HasInstance(roleId, target.PublicId))
        {
            // Not a failure: the role IS varied there, which is what the drop asked for. Select it so
            // the editor shows the set they were reaching for.
            LoadShopsForSelectedRole(target.PublicId);
            ReportSuccess(_localization.Format("Permission.AlreadyVaried", roleName, target.Name));
            return;
        }

        var result = RolePermissionStore.Instance.AddInstance(roleId, target.PublicId);

        if (result != RoleOperationResult.Success)
        {
            ReportFailure(Describe(result));
            return;
        }

        LoadRoles(roleId);
        LoadShopsForSelectedRole(target.PublicId);
        ReportSuccess(_localization.Format("Permission.InstanceAdded", roleName, target.Name));
    }

    /// <summary>The row a hit-tested element sits in, or null for the list's blank space.</summary>
    private static RoleRow? FindRoleRow(DependencyObject source) => FindRow<RoleRow>(source);

    private static ShopRow? FindShopRow(DependencyObject source) => FindRow<ShopRow>(source);

    private static T? FindRow<T>(DependencyObject source) where T : class
    {
        for (var node = source; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is ListBoxItem item && item.DataContext is T row)
                return row;
        }

        return null;
    }

    // ── 3. what it allows ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fills the editor with whichever set is being looked at: the role's default, or its instance in
    /// the selected shop.
    /// </summary>
    private void LoadCapabilityEditor()
    {
        _toggles = new List<CapabilityToggle>();
        CapabilityGroups.ItemsSource = null;

        if (SelectedRole is not { } row || RolePermissionStore.Instance.Find(row.RoleId) is not { } role)
        {
            ShowEditorEmpty(_localization["Permission.PickARole"]);
            return;
        }

        var store = RolePermissionStore.Instance;
        var shop = SelectedShop;
        var editingInstance = shop is { HasInstance: true };

        var granted = store.CapabilitiesFor(role, editingInstance ? shop!.PublicId : null);

        // The heading is the one thing standing between "narrow this branch" and "narrow every
        // branch", so it names the target explicitly rather than relying on which row is highlighted.
        EditorHeading.Text = editingInstance
            ? _localization.Format("Permission.EditingInstance", row.Name, shop!.Name)
            : _localization.Format("Permission.EditingDefault", row.Name);

        EditorHint.Text = _localization[editingInstance
            ? "Permission.EditingInstanceHint"
            : "Permission.EditingDefaultHint"];

        var editable = !row.IsAdministrator;

        _toggles = CapabilityCatalog.All
            .Select(entry => new CapabilityToggle(
                entry.Capability,
                _localization[entry.NameKey],
                _localization[entry.DescriptionKey],
                granted.Contains(entry.Capability),
                editable && CapabilityCatalog.IsGrantable(entry.Capability)))
            .ToList();

        CapabilityGroups.ItemsSource = CapabilityCatalog.Groups
            .Select(group => new CapabilityGroupRow(
                _localization[CapabilityCatalog.GroupNameKey(group)],
                _toggles.Where(toggle => CapabilityCatalog.Entry(toggle.Capability).Group == group).ToList()))
            .Where(group => group.Toggles.Count > 0)
            .ToList();

        EditorEmptyText.Visibility = Visibility.Collapsed;
        SaveCapabilitiesButton.IsEnabled = editable;

        // Restoring only means something for a SHIPPED role's default: a custom role has no shipped
        // set to go back to, and an instance is restored by removing it.
        RestoreDefaultsButton.IsEnabled = editable && row.IsBuiltIn && !editingInstance;
    }

    private void ShowEditorEmpty(string message)
    {
        EditorHeading.Text = string.Empty;
        EditorHint.Text = string.Empty;
        EditorEmptyText.Text = message;
        EditorEmptyText.Visibility = Visibility.Visible;
        SaveCapabilitiesButton.IsEnabled = false;
        RestoreDefaultsButton.IsEnabled = false;
    }

    private void OnSaveCapabilitiesClick(object sender, RoutedEventArgs e)
    {
        if (SelectedRole is not { IsAdministrator: false } role)
            return;

        var chosen = _toggles.Where(toggle => toggle.IsGranted).Select(toggle => toggle.Capability).ToList();
        var shop = SelectedShop;
        var editingInstance = shop is { HasInstance: true };

        var result = editingInstance
            ? RolePermissionStore.Instance.SetInstanceCapabilities(role.RoleId, shop!.PublicId, chosen)
            : RolePermissionStore.Instance.SetCapabilities(role.RoleId, chosen);

        if (result != RoleOperationResult.Success)
        {
            ReportFailure(Describe(result));
            return;
        }

        LoadRoles(role.RoleId);
        LoadShopsForSelectedRole(shop?.PublicId);

        ReportSuccess(editingInstance
            ? _localization.Format("Permission.InstanceSaved", role.Name, shop!.Name)
            : _localization.Format("Permission.DefaultSaved", role.Name));
    }

    private void OnRestoreDefaultsClick(object sender, RoutedEventArgs e)
    {
        if (SelectedRole is not { IsAdministrator: false, IsBuiltIn: true } role)
            return;

        var result = RolePermissionStore.Instance.RestoreDefaults(role.RoleId);

        if (result != RoleOperationResult.Success)
        {
            ReportFailure(Describe(result));
            return;
        }

        LoadRoles(role.RoleId);
        ReportSuccess(_localization.Format("Permission.DefaultsRestored", role.Name));
    }

    // ── plumbing ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Asks for a role name in a small modal built here rather than in a window file of its own.
    /// </summary>
    /// <remarks>
    /// One text box and two buttons; a whole XAML window would be more to keep in step with the theme
    /// than it is worth. Styled from the theme's own resources so it does not read as a different
    /// application.
    /// </remarks>
    private bool AskForName(string title, string current, out string name)
    {
        var box = new TextBox { Text = current, MinWidth = 280, Margin = new Thickness(0, 10, 0, 14) };

        var ok = new Button
        {
            Content = _localization["Permission.Apply"],
            Style = (Style)FindResource("PrimaryButton"),
            MinWidth = 96,
            IsDefault = true,
        };

        var cancel = new Button
        {
            Content = _localization["Shop.Picker.Cancel"],
            MinWidth = 96,
            Margin = new Thickness(8, 0, 0, 0),
            IsCancel = true,
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var body = new StackPanel { Margin = new Thickness(22) };
        body.Children.Add(new TextBlock
        {
            Text = _localization["Permission.NewRoleName"],
            FontSize = 12,
            Foreground = (Brush)FindResource("TextBodyBrush"),
        });
        body.Children.Add(box);
        body.Children.Add(buttons);

        var dialog = new Window
        {
            Title = title,
            Content = body,
            Owner = this,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (Brush)FindResource("SurfaceBrush"),
        };

        ok.Click += (_, _) => dialog.DialogResult = true;
        dialog.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };

        var confirmed = dialog.ShowDialog() is true;
        name = box.Text.Trim();

        return confirmed && name.Length > 0;
    }

    private string Describe(RoleOperationResult result) => result switch
    {
        RoleOperationResult.NameRequired => _localization["Permission.NameRequired"],
        RoleOperationResult.NameTaken => _localization["Permission.NameTaken"],
        RoleOperationResult.NotFound => _localization["Permission.RoleGone"],
        RoleOperationResult.Protected => _localization["Permission.RoleProtected"],
        _ => string.Empty
    };

    private static bool IdMatches(string? left, string? right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private void ReportSuccess(string message) => Report(message, Color.FromRgb(0x04, 0x78, 0x57));

    private void ReportFailure(string message) => Report(message, Color.FromRgb(0xB9, 0x1C, 0x1C));

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static",
        Justification = "False positive: StatusText is an x:Name instance field from the XAML-generated " +
                        "partial, which SonarLint's single-file pass cannot see.")]
    private void Report(string message, Color colour)
    {
        StatusText.Text = message;
        StatusText.Foreground = new SolidColorBrush(colour);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}

/// <summary>One role in the catalogue column.</summary>
/// <remarks>
/// Top-level and internal rather than nested and private, for the reason <c>ShopPickerRow</c> is:
/// every member is reached only through <c>{Binding}</c>, which static analysis cannot see, so as
/// private members they each read as dead code and would need a suppression apiece to survive.
/// </remarks>
internal sealed record RoleRow(
    string RoleId,
    string Name,
    string Detail,
    bool IsAdministrator,
    bool IsBuiltIn,
    Visibility LockedVisibility,
    string LockedText);

/// <summary>One shop in the "where is this varied" column.</summary>
internal sealed record ShopRow(
    Guid PublicId,
    string Name,
    string Detail,
    bool HasInstance,
    string StateText,
    Brush StateBackground,
    Brush StateForeground);

/// <summary>One capability tick box. Mutable, because the box writes <see cref="IsGranted"/> back.</summary>
internal sealed class CapabilityToggle
{
    public CapabilityToggle(
        AppCapability capability, string name, string detail, bool isGranted, bool isEditable)
    {
        Capability = capability;
        Name = name;
        Detail = detail;
        IsGranted = isGranted;
        IsEditable = isEditable;
    }

    public AppCapability Capability { get; }

    public string Name { get; }

    public string Detail { get; }

    public bool IsGranted { get; set; }

    /// <summary>False for the three nobody may be given, and for the administrator's whole list.</summary>
    public bool IsEditable { get; }
}

/// <summary>One heading and the capabilities under it.</summary>
internal sealed record CapabilityGroupRow(string GroupName, IReadOnlyList<CapabilityToggle> Toggles);
