namespace LeeYongeOrdering.Models;

/// <summary>
/// What a signed-in user is allowed to do. Only <see cref="Admin"/> is issued today and nothing is
/// gated on the value yet — the roles exist so that adding staff accounts later is a data change
/// plus a management screen, with no credential-file migration.
///
/// When gating does arrive, put the decisions behind named capability checks on the session (e.g.
/// "can manage shops") rather than scattering <c>role == Admin</c> comparisons through the UI.
/// </summary>
public enum UserRole
{
    /// <summary>Full access, including creating and switching shops.</summary>
    Admin = 0,

    /// <summary>Reserved: runs a single shop's day-to-day operation.</summary>
    Manager = 1,

    /// <summary>Reserved: takes orders, with no configuration access.</summary>
    Staff = 2
}
