using System.ComponentModel;
using System.Runtime.CompilerServices;
using CameywareOrder.Localization;
using CameywareOrder.Models;

namespace CameywareOrder.Views;

/// <summary>
/// The trees the permission panel draws: shops on the right, holding the roles and what each role
/// may do, and accounts on the left, holding the roles they have been given in each shop.
/// </summary>
/// <remarks>
/// ONE <see cref="RoleNode"/> PER ROLE, shared by every shop that lists it. Roles are defined once
/// for the whole installation, so a copy per shop would let the same role show two different
/// capability sets on one screen, and only one of them could be the one that got saved. Sharing the
/// instance makes that impossible: ticking a box under one shop is visibly the same tick under every
/// other, which is exactly the fact an administrator needs to understand before they change it.
/// </remarks>
internal sealed class ShopNode
{
    public ShopNode(string name, string details, IReadOnlyList<RoleNode> roles)
    {
        Name = name;
        Details = details;
        Roles = roles;
    }

    public string Name { get; }

    public string Details { get; }

    public IReadOnlyList<RoleNode> Roles { get; }
}

/// <summary>One role, and its capabilities grouped the way the catalog groups them.</summary>
internal sealed class RoleNode : PermissionNode
{
    public RoleNode(
        RoleDefinition role, ILocalizedText text, int holders, IReadOnlyList<CapabilityGroupNode> groups)
    {
        RoleId = role.Id;
        Name = role.ResolveName(text);
        IsEditable = !role.IsLocked;
        IsBuiltIn = role.IsBuiltIn;
        IsAdministrator = role.IsAdministratorRole;
        Groups = groups;
        Holders = holders;
        _text = text;
    }

    private readonly ILocalizedText _text;

    public string RoleId { get; }

    public string Name { get; }

    /// <summary>False for the administrator, whose rights are not a decision anybody gets to make.</summary>
    public bool IsEditable { get; }

    /// <summary>Shipped with the application: its capabilities can be changed, the role cannot be deleted.</summary>
    public bool IsBuiltIn { get; }

    public bool IsAdministrator { get; }

    public IReadOnlyList<CapabilityGroupNode> Groups { get; }

    /// <summary>How many accounts hold this role, anywhere in the installation.</summary>
    public int Holders { get; }

    /// <summary>The capabilities currently ticked, across every group.</summary>
    public IReadOnlyList<AppCapability> Selected()
        => Groups.SelectMany(group => group.Capabilities)
            .Where(capability => capability.IsGranted)
            .Select(capability => capability.Capability)
            .ToList();

    /// <summary>
    /// "Permissions: 7, held by 3" — recomputed as boxes are ticked.
    /// </summary>
    /// <remarks>
    /// The counts are written as "Permissions: {0}" rather than "{0} permissions" on purpose. A role
    /// with exactly one capability would otherwise read "1 permissions" in every language that
    /// inflects a noun, and a plural rule per language is a lot of machinery for a status line.
    /// </remarks>
    public string Summary => _text.JoinList(new[]
    {
        _text.Format("Permission.RoleSummary", Selected().Count),
        _text.Format("Permission.RoleHolders", Holders)
    });

    /// <summary>Called by a child when its tick moved, so the role's own summary follows it.</summary>
    public void OnCapabilityChanged() => Raise(nameof(Summary));
}

/// <summary>One heading in the capability list — Orders, Reporting, and so on.</summary>
internal sealed class CapabilityGroupNode
{
    public CapabilityGroupNode(string name, IReadOnlyList<CapabilityNode> capabilities)
    {
        Name = name;
        Capabilities = capabilities;
    }

    public string Name { get; }

    public IReadOnlyList<CapabilityNode> Capabilities { get; }
}

/// <summary>One capability, as a tick box under a role.</summary>
internal sealed class CapabilityNode : PermissionNode
{
    private bool _isGranted;

    public CapabilityNode(CapabilityEntry entry, ILocalizedText text, bool isGranted, bool isEditable)
    {
        Capability = entry.Capability;
        Name = text[entry.NameKey];
        Detail = text[entry.DescriptionKey];
        IsEditable = isEditable;
        _isGranted = isGranted;
    }

    /// <summary>Set by the role node when it is built, so a tick can tell its parent to re-summarise.</summary>
    public RoleNode? Owner { get; set; }

    public AppCapability Capability { get; }

    public string Name { get; }

    public string Detail { get; }

    /// <summary>
    /// False for a capability nobody can be given — the three that belong to the administrator
    /// alone — and for every capability of the administrator role itself.
    /// </summary>
    public bool IsEditable { get; }

    public bool IsGranted
    {
        get => _isGranted;
        set
        {
            if (_isGranted == value)
                return;

            _isGranted = value;
            Raise();
            Owner?.OnCapabilityChanged();
        }
    }
}

/// <summary>One account on the left-hand tree, and the shops it belongs to.</summary>
internal sealed class AccountNode
{
    public AccountNode(string userName, string label, string details, bool isAdministrator,
        IReadOnlyList<AccountShopNode> shops)
    {
        UserName = userName;
        Label = label;
        Details = details;
        IsAdministrator = isAdministrator;
        Shops = shops;
        Initial = UserPresentation.Initial(label);
        AvatarBrush = UserPresentation.AvatarBrush(label);
    }

    public string UserName { get; }

    public string Label { get; }

    public string Details { get; }

    /// <summary>First letter of the name, for the avatar tile.</summary>
    public string Initial { get; }

    /// <summary>
    /// The same tile colour this person carries on every other screen. From the shared
    /// <see cref="UserPresentation"/> rather than a colour of this panel's own: an avatar that
    /// changed hue between the roster and the permission panel would stop being recognisable, which
    /// is the only thing it is for.
    /// </summary>
    public System.Windows.Media.Brush AvatarBrush { get; }

    /// <summary>The administrator holds everything everywhere; their tree carries no tick boxes.</summary>
    public bool IsAdministrator { get; }

    public IReadOnlyList<AccountShopNode> Shops { get; }
}

/// <summary>One account's standing in one shop: which roles it holds there.</summary>
internal sealed class AccountShopNode
{
    public AccountShopNode(Guid shopPublicId, string shopName, IReadOnlyList<RoleToggle> roles)
    {
        ShopPublicId = shopPublicId;
        ShopName = shopName;
        Roles = roles;
    }

    public Guid ShopPublicId { get; }

    public string ShopName { get; }

    public IReadOnlyList<RoleToggle> Roles { get; }
}

/// <summary>Change notification for the nodes that are edited on screen.</summary>
/// <remarks>
/// A tree node needs it for one specific reason: the SAME role object appears under every shop, so
/// a tick under one of them has to be seen by the copies drawn under the others. Without
/// notification the screen would show one role in two contradictory states.
/// </remarks>
internal abstract class PermissionNode : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName ?? string.Empty));
}
