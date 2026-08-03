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
/// <summary>
/// Shape of <c>credentials.json</c>. A LIST from the outset even though only one account is seeded,
/// so adding accounts later needs no file-format change or migration.
/// </summary>
public sealed class CredentialFile
{
    /// <summary>See <c>AuthenticationService.CurrentSchemaVersion</c>. Absent (0) means version 1.</summary>
    public int SchemaVersion { get; set; }

    /// <summary>
    /// Every account this installation has ever seeded. It is what makes deleting a seeded account
    /// permanent — without it the next launch would create it again, and an administrator would
    /// have no way to remove an account at all.
    /// </summary>
    public List<string> ProvisionedAccounts { get; set; } = new();

    public List<CredentialRecord> Users { get; set; } = new();
}

public sealed class CredentialRecord
{
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// The person's given name. This is what the application greets them by, so it is the half that
    /// matters most — "Hi Tina" rather than "Hi tina.zhang".
    /// </summary>
    public string? FirstName { get; set; }

    /// <summary>The person's family name. Optional: plenty of people are known by one name here.</summary>
    public string? LastName { get; set; }

    public DateTime? BirthDate { get; set; }

    /// <summary>
    /// How to reach this person. Account-level, NOT per shop: someone who works at two branches has
    /// one phone and one mailbox, and storing them per membership would let the two disagree.
    /// </summary>
    /// <remarks>
    /// Both optional and stored null when blank, so an existing credentials file is already valid
    /// under this schema — no version bump and no migration; a record simply has no number yet.
    /// </remarks>
    public string? PhoneNumber { get; set; }

    public string? Email { get; set; }

    /// <summary>Full access everywhere. Held by exactly one account, which cannot be deleted.</summary>
    public bool IsAdministrator { get; set; }

    /// <summary>The shops this account belongs to, and its standing in each.</summary>
    public List<ShopMembership> Memberships { get; set; } = new();

    public int Iterations { get; set; }
    public string Salt { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;

    /// <summary>
    /// Whether this account must replace its password before it can sign in again.
    /// </summary>
    /// <remarks>
    /// Set when an account is created and when an administrator resets a password — in both cases
    /// somebody other than the account's owner chose the value and knows it. Cleared only by
    /// <see cref="AuthenticationService.ChangeOwnPassword"/>, which is the only path where the
    /// person typing the new password is the person it belongs to.
    /// </remarks>
    public bool MustChangePassword { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// The single global role written by schema version 1. Read once by the upgrade and then
    /// cleared; it is omitted from the file rather than written as null so a migrated file carries
    /// no dead field.
    /// </summary>
    [JsonPropertyName("Role")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UserRole? LegacyRole { get; set; }

    /// <summary>
    /// The flat (shop, role) list written by schema version 2, before activation and shift data
    /// needed a record per shop rather than per role. Folded into <see cref="Memberships"/> on load
    /// and then cleared.
    /// </summary>
    [JsonPropertyName("Assignments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<LegacyShopAssignment>? LegacyAssignments { get; set; }

    /// <summary>
    /// The single name field written by schema versions 1-3, before a name was split into
    /// <see cref="FirstName"/> and <see cref="LastName"/>. Split on load and then cleared.
    /// </summary>
    /// <remarks>
    /// It has to stay declared. Removing the property would make System.Text.Json discard the value
    /// on exactly the load that was supposed to migrate it, so every existing person would silently
    /// lose their name — and the file would be rewritten without it before anybody noticed.
    /// </remarks>
    [JsonPropertyName("DisplayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyDisplayName { get; set; }
}

/// <summary>
/// One person's membership of one shop: the role(s) they hold there, whether they still work there,
/// when they started, when they were delisted, and the shift they work.
/// </summary>
/// <remarks>
/// Keyed on <see cref="Shop.PublicId"/> rather than <see cref="Shop.Id"/> for the reason documented
/// on that property: this file lives OUTSIDE the database, and whole databases move between
/// machines, where the local autoincrement ids collide.
///
/// <see cref="RoleIds"/> is a SET because holding several roles in one shop is legal — what the
/// person may do is the UNION of what those roles grant, which is why there is no longer a
/// "strongest role" to resolve. Activation lives here rather than on the account because suspending
/// someone at one branch must not cost them their job at another.
/// </remarks>
public sealed class ShopMembership
{
    public Guid ShopPublicId { get; set; }

    /// <summary>
    /// The roles held here, by <see cref="RoleDefinition.Id"/>. An id naming a role that no longer
    /// exists grants nothing; the catalog is the authority, not this list.
    /// </summary>
    public List<string> RoleIds { get; set; } = new();

    /// <summary>
    /// The fixed <see cref="UserRole"/> list written by schema versions 2-4, before an installation
    /// could define roles of its own. Converted to <see cref="RoleIds"/> on load and then cleared.
    /// </summary>
    /// <remarks>
    /// It has to stay declared, for the same reason <c>CredentialRecord.LegacyDisplayName</c> does:
    /// removing the property would make System.Text.Json discard the value on exactly the load that
    /// was supposed to migrate it, so every membership in the installation would silently lose its
    /// roles and the file would be rewritten without them before anybody noticed.
    /// </remarks>
    [JsonPropertyName("Roles")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<UserRole>? LegacyRoles { get; set; }

    /// <summary>False once the member has been delisted from this shop. Defaults to true.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>The day they started working at this shop.</summary>
    public DateTime? JoinedOn { get; set; }

    /// <summary>Stamped automatically when the membership is deactivated; cleared on reactivation.</summary>
    public DateTime? DeactivatedOn { get; set; }

    /// <summary>Start of their daily shift.</summary>
    public TimeOnly? ShiftStart { get; set; }

    /// <summary>End of their daily shift.</summary>
    public TimeOnly? ShiftEnd { get; set; }
}

/// <summary>One (shop, role) pair as schema version 2 stored it. Read-only history; do not extend.</summary>
public sealed class LegacyShopAssignment
{
    public Guid ShopPublicId { get; set; }

    public UserRole Role { get; set; }
}
