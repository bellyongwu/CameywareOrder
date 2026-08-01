namespace CameywareOrder.Models;

/// <summary>
/// What a signed-in user is allowed to do.
///
/// <see cref="Admin"/> is an ACCOUNT-level property (see <c>CredentialRecord.IsAdministrator</c>) —
/// it is never assigned to a shop, because an administrator already has every right in every shop.
/// <see cref="Manager"/> and <see cref="Staff"/> are per-shop: an account holds a set of them per
/// branch, so the same person can run one shop and take orders in another. Holding both in one shop
/// is legal and resolves to Manager.
///
/// DECLARATION ORDER IS LOAD-BEARING: the values are ordered strongest-first, and
/// <c>AuthenticationService.StrongestRole</c> resolves the effective role by taking the minimum.
/// Inserting a value in the middle would silently re-rank the existing ones.
///
/// Decisions are made through named capability properties on <c>AuthenticationService</c>
/// (<c>CanConfigureShop</c>, <c>CanUseDataTools</c>, …) rather than <c>role == Manager</c>
/// comparisons scattered through the UI, so a rule change has one home.
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
