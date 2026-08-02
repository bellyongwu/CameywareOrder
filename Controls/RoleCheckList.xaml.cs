using System.Windows.Controls;
using CameywareOrder.Localization;
using CameywareOrder.Views;

namespace CameywareOrder.Controls;

/// <summary>
/// The roles an installation defines, as a set of tick boxes — "which roles does this person hold
/// in this shop".
/// </summary>
/// <remarks>
/// Deliberately dumb: it renders the catalog and reports what is ticked. It does not save, does not
/// know which shop it is describing, and holds no opinion on whether an empty selection is legal —
/// that is the roster screen's rule (a member with no role is not a member), and it is enforced once
/// in <c>AuthenticationService</c> rather than in every screen that shows this control.
/// </remarks>
public partial class RoleCheckList : UserControl
{
    private List<RoleToggle> _toggles = new();

    public RoleCheckList()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Shows every assignable role, ticking the ones already held.
    /// </summary>
    /// <param name="text">
    /// Where the role names are read from — <see cref="ILocalizedText"/> rather than the
    /// localization singleton, so a panel previewing another language names them in that language.
    /// </param>
    /// <param name="heldRoleIds">The roles currently held, or null for a blank slate.</param>
    public void Load(ILocalizedText text, IEnumerable<string>? heldRoleIds)
    {
        _toggles = RoleToggle.ForMembership(text, heldRoleIds);
        RoleItems.ItemsSource = _toggles;
    }

    /// <summary>The ids of the ticked roles.</summary>
    public IReadOnlyList<string> SelectedRoleIds => RoleToggle.Selected(_toggles);

    /// <summary>Whether anything is ticked at all.</summary>
    public bool HasSelection => _toggles.Exists(toggle => toggle.IsGranted);

    /// <summary>Ticks one role and clears the rest — the default a new member is offered.</summary>
    public void SelectOnly(string roleId)
    {
        foreach (var toggle in _toggles)
            toggle.IsGranted = string.Equals(toggle.RoleId, roleId, StringComparison.OrdinalIgnoreCase);

        // The toggles are plain objects, so nothing has told the boxes their value moved.
        RoleItems.ItemsSource = null;
        RoleItems.ItemsSource = _toggles;
    }
}
