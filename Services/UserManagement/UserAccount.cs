using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CameywareOrder.Models;
using Path = System.IO.Path;
using CameywareOrder.Configuration;
namespace CameywareOrder.Services;

// Lifted out of AuthenticationService.cs in v9.3.0, which held a service and twelve types.
// The namespace is unchanged — the folder scheme is for a reader, and moving namespaces would
// touch every using in the application for no gain (Architecture.md).
/// <summary>The signed-in user, as the rest of the app sees them.</summary>
public sealed record UserAccount(
    string UserName,
    string? FirstName,
    string? LastName,
    string? PhoneNumber,
    string? Email,
    bool IsAdministrator,
    IReadOnlyList<ShopMembership> Memberships)
{
    /// <summary>Both name halves as one string; empty when the account has no name at all.</summary>
    public string FullName => PersonName.Full(FirstName, LastName);

    /// <summary>What to call this account on screen — its name if it has one, else the login.</summary>
    public string DisplayLabel => PersonName.Label(FirstName, LastName, UserName);

    /// <summary>What to greet this person by: their first name.</summary>
    public string GreetingName => PersonName.Greeting(FirstName, LastName, UserName);

    /// <summary>
    /// The distinct roles this account holds across the shops it is ACTIVE in, in catalog order.
    /// </summary>
    /// <remarks>
    /// Active memberships only. A role at a branch that has delisted this person is not a role they
    /// hold, and listing it beside their name would promise access the shop picker will not offer.
    /// An administrator reports the administrator role alone.
    ///
    /// Resolved through the catalog rather than returned as raw ids, so a role that has been deleted
    /// simply stops being listed instead of showing somebody a name that means nothing.
    ///
    /// A method rather than a property because it builds a new collection on every call: a property
    /// that allocates invites being read in a loop as though it were a field.
    /// </remarks>
    public IReadOnlyList<RoleDefinition> HeldRoles()
    {
        if (IsAdministrator)
            return new[] { BuiltInRoles.Administrator() };

        var held = Memberships
            .Where(membership => membership.IsActive)
            .SelectMany(membership => membership.RoleIds)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return RolePermissionStore.Instance.All()
            .Where(role => held.Contains(role.Id))
            .ToArray();
    }
}

/// <summary>One person's place in one shop, as the roster screen edits it.</summary>
public sealed record StoreMember(
    string UserName,
    string? FirstName,
    string? LastName,
    DateTime? BirthDate,
    string? PhoneNumber,
    string? Email,
    bool IsAdministrator,
    ShopMembership Membership)
{
    /// <summary>What to call this person on screen — their name if they have one, else the account.</summary>
    public string DisplayLabel => PersonName.Label(FirstName, LastName, UserName);
}

/// <summary>
/// The editable half of a member: the account-level profile plus their standing in ONE shop.
/// Grouped into one object rather than passed as eight parameters.
/// </summary>
public sealed record MemberProfile(
    string? FirstName,
    string? LastName,
    DateTime? BirthDate,
    string? PhoneNumber,
    string? Email,
    IReadOnlyList<string> RoleIds,
    bool IsActive,
    DateTime? JoinedOn,
    TimeOnly? ShiftStart,
    TimeOnly? ShiftEnd);

/// <summary>
/// The account-level half of a person, as the administrator's screen edits it: who they are, what
/// they sign in as, and how to reach them.
/// </summary>
/// <remarks>
/// <see cref="NewUserName"/> is the login the account should have AFTER the save — normally the one
/// it already has. It travels with the rest so one Save applies the whole card: a rename that
/// succeeded while the phone number failed would leave the screen describing an account that no
/// longer answers to that name.
/// </remarks>
public sealed record AccountProfile(
    string NewUserName,
    string? FirstName,
    string? LastName,
    string? PhoneNumber,
    string? Email);
