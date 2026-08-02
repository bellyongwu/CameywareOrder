using System.Windows.Media;
using CameywareOrder.Localization;
using CameywareOrder.Models;

namespace CameywareOrder.Views;

/// <summary>
/// How accounts and roles are presented in lists: the localized role name, and the coloured avatar
/// tile that stands in for a per-user or per-shop image.
/// </summary>
/// <remarks>
/// Shared rather than repeated because three screens render the same two things — the shop picker,
/// the user manager and the main window's toolbar. The role-name switch in particular had already
/// been copied twice, and a copy that falls behind shows a user the wrong role, which is exactly the
/// kind of mistake nobody reports as a bug.
/// </remarks>
internal static class UserPresentation
{
    /// <summary>
    /// Avatar colours. Picked by name hash so a given account or shop keeps the same tile between
    /// launches — a colour that moved on every load would be noise rather than a cue.
    /// Frozen so the brushes are shareable and never re-rendered as list rows are rebuilt.
    /// </summary>
    private static readonly Brush[] AvatarBrushes =
    {
        Frozen("#4F46E5"), Frozen("#0891B2"), Frozen("#B45309"),
        Frozen("#047857"), Frozen("#BE185D"), Frozen("#7C3AED")
    };

    /// <summary>
    /// Several roles as one phrase — "Manager, Staff" — punctuated the way the current language
    /// punctuates a list, and naming the "no role at all" case rather than rendering blank.
    /// </summary>
    /// <remarks>
    /// A LIST, not a single name, because a membership grants the UNION of its roles: there is no
    /// longer a strongest one to print, and printing only the first would describe a person by a
    /// subset of what they can actually do.
    /// </remarks>
    public static string RoleList(ILocalizedText text, IEnumerable<RoleDefinition> roles)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(roles);

        var names = roles.Select(role => role.ResolveName(text)).ToList();

        return names.Count == 0 ? text["Shop.Role.None"] : text.JoinList(names);
    }

    /// <summary>First character of a name, upper-cased, for an avatar tile.</summary>
    public static string Initial(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        return trimmed.Length == 0 ? "?" : trimmed[..1].ToUpperInvariant();
    }

    /// <summary>A stable avatar colour for a name.</summary>
    public static Brush AvatarBrush(string? name)
    {
        var key = name ?? string.Empty;
        return AvatarBrushes[Math.Abs(key.GetHashCode(StringComparison.Ordinal)) % AvatarBrushes.Length];
    }

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
