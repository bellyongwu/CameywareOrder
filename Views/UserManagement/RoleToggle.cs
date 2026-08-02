using CameywareOrder.Localization;
using CameywareOrder.Models;
using CameywareOrder.Services;

namespace CameywareOrder.Views;

/// <summary>
/// One role, offered as a tick box: its name, what it grants in one phrase, and whether the person
/// being edited holds it.
/// </summary>
/// <remarks>
/// Built from the role CATALOG rather than written into a screen, which is the whole point of the
/// change: the two roster screens used to carry a hard-coded Manager box and a hard-coded Staff box,
/// so a role defined afterwards could not be given to anybody — and worse, saving a member from
/// either screen would have written back only those two and silently STRIPPED any other role the
/// person held.
///
/// A plain settable property rather than an observable one: WPF writes a two-way
/// <c>CheckBox.IsChecked</c> straight back into it, and nothing on screen has to react to the change.
/// </remarks>
internal sealed class RoleToggle
{
    private RoleToggle(RoleDefinition role, ILocalizedText text, bool isGranted)
    {
        RoleId = role.Id;
        Name = role.ResolveName(text);
        Detail = text.Format("Permission.RoleSummary", role.Capabilities.Count);
        IsGranted = isGranted;
    }

    public string RoleId { get; }

    public string Name { get; }

    /// <summary>How much this role grants, so a name alone does not have to carry the meaning.</summary>
    public string Detail { get; }

    public bool IsGranted { get; set; }

    /// <summary>
    /// Every assignable role, ticked where the given membership holds it. Unknown ids in
    /// <paramref name="heldRoleIds"/> are simply not represented — a role that has been deleted
    /// cannot be re-granted by a screen that no longer offers it.
    /// </summary>
    public static List<RoleToggle> ForMembership(ILocalizedText text, IEnumerable<string>? heldRoleIds)
    {
        ArgumentNullException.ThrowIfNull(text);

        var held = new HashSet<string>(
            heldRoleIds ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        return RolePermissionStore.Instance.Assignable()
            .Select(role => new RoleToggle(role, text, held.Contains(role.Id)))
            .ToList();
    }

    /// <summary>The ids of the ticked boxes.</summary>
    public static List<string> Selected(IEnumerable<RoleToggle> toggles)
    {
        ArgumentNullException.ThrowIfNull(toggles);
        return toggles.Where(toggle => toggle.IsGranted).Select(toggle => toggle.RoleId).ToList();
    }
}
