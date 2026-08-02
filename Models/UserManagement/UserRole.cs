namespace CameywareOrder.Models;

/// <summary>
/// THE ROLE SET THE APPLICATION USED TO HAVE, kept for one purpose: reading a
/// <c>credentials.json</c> written before roles became data. Nothing decides anything from this type
/// any more.
///
/// It was three fixed values, and "what may this user do" was a comparison against them
/// (<c>role == Manager</c>) wrapped in a named property. That is a permission model an installation
/// cannot change without a new build. Roles are now <see cref="RoleDefinition"/> records in
/// <c>roles.json</c>, each a set of <see cref="AppCapability"/> values, and a membership names them
/// by id.
///
/// DO NOT ADD VALUES HERE. A new role is data — define it in the permission panel. The only reason
/// this enum still exists is that the numbers below are what old files literally contain, so
/// deleting it would make those files unreadable.
/// </summary>
public enum UserRole
{
    /// <summary>Full access to the whole installation: every shop, its settings, data and accounts.</summary>
    Admin = 0,

    /// <summary>Runs a shop's day-to-day operation, including its configuration.</summary>
    Manager = 1,

    /// <summary>Takes orders in a shop, with no access to its configuration.</summary>
    Staff = 2
}

/// <summary>
/// Maps the retired <see cref="UserRole"/> values onto the role ids that replaced them.
/// </summary>
/// <remarks>
/// Lives beside the legacy type rather than with the migration that calls it, because it is only
/// meaningful as a statement about the OLD shape — and because there are two callers (the version-1
/// global role and the version-2 flat assignments), which is exactly how a mapping like this comes
/// to be written twice and then disagree.
/// </remarks>
public static class LegacyRoleIds
{
    /// <summary>The role id a stored <see cref="UserRole"/> becomes.</summary>
    public static string For(UserRole role) => role switch
    {
        UserRole.Admin => RoleDefinition.AdministratorId,
        UserRole.Manager => RoleDefinition.ManagerId,
        _ => RoleDefinition.StaffId
    };
}
