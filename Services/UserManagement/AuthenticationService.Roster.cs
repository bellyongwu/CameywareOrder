using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CameywareOrder.Models;
using Path = System.IO.Path;
using CameywareOrder.Configuration;
namespace CameywareOrder.Services;

// Roster — one responsibility of AuthenticationService, split out in v9.3.0.
// A PARTIAL rather than a separate type: these members read the same private state as the rest of
// the service, and threading it through a new class's constructor would be shape for its own sake.
public sealed partial class AuthenticationService
{
    // --- Store roster (one shop; manager or administrator) --------------------------------------

    /// <summary>
    /// Everyone holding a membership in one shop, active first and then by display name. Deactivated
    /// members stay in the list: the screen's job includes showing who left and when.
    /// </summary>
    public IReadOnlyList<StoreMember> ListMembers(Guid shopPublicId)
        => _file.Users
            .Select(record => new
            {
                Record = record,
                Membership = record.Memberships.Find(m => m.ShopPublicId == shopPublicId)
            })

            .Where(entry => entry.Membership is not null)
            .Select(entry => new StoreMember(
                entry.Record.UserName,
                entry.Record.FirstName,
                entry.Record.LastName,
                entry.Record.BirthDate,
                entry.Record.PhoneNumber,
                entry.Record.Email,
                entry.Record.IsAdministrator,
                Clone(entry.Membership!)))
            .OrderByDescending(member => member.Membership.IsActive)
            .ThenBy(member => member.DisplayLabel, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>
    /// Creates an account AND its membership in one shop, which is what "add someone to my store"
    /// means for a manager: the account exists so they can sign in, and it exists here so they can
    /// sign in to somewhere.
    /// </summary>
    public AccountOperationResult AddMember(
        Guid shopPublicId, string userName, string password, MemberProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var name = (userName ?? string.Empty).Trim();

        if (string.IsNullOrEmpty(name))
            return AccountOperationResult.UserNameRequired;

        var rejection = CheckPassword(name, password);

        if (rejection != AccountOperationResult.Success)
            return rejection;

        if (profile.RoleIds.Count == 0)
            return AccountOperationResult.RoleRequired;

        if (FindRecord(name) is not null)
            return AccountOperationResult.UserNameTaken;

        var record = CreateRecord(name, password, isAdministrator: false);
        record.Memberships.Add(new ShopMembership { ShopPublicId = shopPublicId });
        _file.Users.Add(record);

        ApplyProfile(record, shopPublicId, profile);

        Save(_file);
        return AccountOperationResult.Success;
    }

    /// <summary>
    /// Updates one member's profile and their membership in ONE shop. Everything about their other
    /// shops is left untouched — that is the whole point of a per-shop roster.
    /// </summary>
    public AccountOperationResult UpdateMember(Guid shopPublicId, string userName, MemberProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var record = FindRecord(userName);

        if (record is null)
            return AccountOperationResult.NotFound;

        if (record.Memberships.TrueForAll(membership => membership.ShopPublicId != shopPublicId))
            return AccountOperationResult.NotFound;

        if (profile.RoleIds.Count == 0)
            return AccountOperationResult.RoleRequired;

        // Deactivating yourself in the shop you are standing in would revoke the screen you are
        // standing on, mid-edit, with no way back short of another administrator.
        if (!profile.IsActive && IsCurrentUser(record.UserName) && _activeShopPublicId == shopPublicId)
            return AccountOperationResult.Protected;

        ApplyProfile(record, shopPublicId, profile);

        Save(_file);
        RefreshCurrentUser(record);
        return AccountOperationResult.Success;
    }

    /// <summary>
    /// Writes a profile onto a record and its membership in one shop, stamping the deactivation time
    /// on the transition rather than taking it from the caller — "when were they delisted" is a fact
    /// about what happened, not a field somebody types.
    /// </summary>
    private static void ApplyProfile(CredentialRecord record, Guid shopPublicId, MemberProfile profile)
    {
        record.FirstName = Blank(profile.FirstName);
        record.LastName = Blank(profile.LastName);
        record.BirthDate = profile.BirthDate;
        record.PhoneNumber = Blank(profile.PhoneNumber);
        record.Email = Blank(profile.Email);

        var membership = record.Memberships.First(candidate => candidate.ShopPublicId == shopPublicId);

        if (membership.IsActive && !profile.IsActive)
            membership.DeactivatedOn = DateTime.Now;
        else if (!membership.IsActive && profile.IsActive)
            membership.DeactivatedOn = null;

        membership.IsActive = profile.IsActive;
        membership.RoleIds = NormalizeRoleIds(profile.RoleIds);
        membership.JoinedOn = profile.JoinedOn;
        membership.ShiftStart = profile.ShiftStart;
        membership.ShiftEnd = profile.ShiftEnd;
    }

    /// <summary>Trimmed, or null when the caller left the field empty — never a blank string.</summary>
    /// <remarks>
    /// "" and null must not both mean "no phone number": one of them would print as an empty
    /// labelled line and the other would be skipped, depending on which screen read it.
    /// </remarks>
    private static string? Blank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Whether the signed-in user may replace another account's password.
    /// </summary>
    /// <remarks>
    /// An administrator always may. A manager may only when the target works exclusively in shops
    /// that same manager runs — otherwise resetting a password from this shop's roster would hand
    /// over an account that also works in a branch the manager has nothing to do with.
    /// </remarks>
    public bool CanSetPasswordFor(string userName)
    {
        if (IsAdministrator)
            return true;

        if (CurrentUser is null)
            return false;

        var target = FindRecord(userName);

        if (target is null || target.IsAdministrator)
            return false;

        // "Shops this user runs" is now a capability question rather than a role comparison: whoever
        // may manage a shop's roster is the person who may reset the passwords of the people on it.
        var catalog = RolePermissionStore.Instance;

        var managedShops = CurrentUser.Memberships
            .Where(membership => membership.IsActive
                && catalog.CapabilitiesFor(membership.RoleIds).Contains(AppCapability.ManageStoreMembers))
            .Select(membership => membership.ShopPublicId)
            .ToHashSet();

        return target.Memberships.TrueForAll(membership => managedShops.Contains(membership.ShopPublicId));
    }

    // --- Account management (administrator only; callers gate, this layer only stores) ----------

    /// <summary>Every account, ordered administrators first and then by name.</summary>
    public IReadOnlyList<UserAccount> ListAccounts()
        => _file.Users
            .Select(ToAccount)
            .OrderByDescending(account => account.IsAdministrator)
            .ThenBy(account => account.UserName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>
    /// Sets an account's contact details without touching any membership.
    /// </summary>
    /// <remarks>
    /// The roster's <see cref="UpdateMember"/> can only reach people who belong to a shop, and
    /// <see cref="CreateAccount"/> deliberately makes accounts that belong to none — so without
    /// this those accounts could never be given a phone number or an address at all.
    ///
    /// Unlike a role change this is safe to apply to one's own account and to the administrator:
    /// it grants nothing. The caller still gates on <see cref="CanManageUsers"/>.
    /// </remarks>
    public AccountOperationResult UpdateAccountContact(string userName, string? phoneNumber, string? email)
    {
        var record = FindRecord(userName);
        if (record is null)
            return AccountOperationResult.NotFound;

        return UpdateAccountProfile(userName, new AccountProfile(
            record.UserName, record.FirstName, record.LastName, phoneNumber, email));
    }

    /// <summary>
    /// Writes the account-level half of a person: their name, the login they sign in with, and how
    /// to reach them. Touches no membership, so unlike a role change it is safe on the administrator
    /// and on one's own account.
    /// </summary>
    /// <remarks>
    /// The rename and the rest are ONE operation on purpose. Applying them separately would let a
    /// rename land while a bad phone number was rejected, leaving the screen describing an account
    /// that no longer answers to the name on it. Everything is validated before anything is written.
    /// </remarks>
    public AccountOperationResult UpdateAccountProfile(string userName, AccountProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var record = FindRecord(userName);
        if (record is null)
            return AccountOperationResult.NotFound;

        var rename = CheckRename(record, profile.NewUserName, out var newUserName);
        if (rename != AccountOperationResult.Success)
            return rename;

        // Decided BEFORE the rename: the session is identified by user name, and after a rename the
        // record no longer matches the name the session was signed in under.
        var isCurrent = IsCurrentUser(record.UserName);

        if (newUserName is not null)
            ApplyRename(record, newUserName);

        record.FirstName = Blank(profile.FirstName);
        record.LastName = Blank(profile.LastName);
        record.PhoneNumber = Blank(profile.PhoneNumber);
        record.Email = Blank(profile.Email);

        Save(_file);

        if (isCurrent)
            AdoptAsCurrentUser(record);

        return AccountOperationResult.Success;
    }

    /// <summary>
    /// Whether <paramref name="record"/> may take <paramref name="requested"/> as its login, and
    /// whether that is even a change. <paramref name="newUserName"/> is null when the name is
    /// unchanged, which is the normal case — most saves are not renames.
    /// </summary>
    /// <remarks>
    /// The administrator's login cannot be changed. That is a product rule rather than a technical
    /// limitation — the administrator is the one account that must remain identifiable and can never
    /// be deleted, demoted or locked out, and a login somebody can edit is one somebody can lose.
    /// The UI disables the box and says so, rather than letting the attempt reach here.
    /// </remarks>
    private AccountOperationResult CheckRename(
        CredentialRecord record, string? requested, out string? newUserName)
    {
        newUserName = null;

        var wanted = (requested ?? string.Empty).Trim();

        if (wanted.Length == 0)
            return AccountOperationResult.UserNameRequired;

        if (string.Equals(wanted, record.UserName, StringComparison.Ordinal))
            return AccountOperationResult.Success;

        if (record.IsAdministrator)
            return AccountOperationResult.Protected;

        if (IsUserNameTaken(wanted, record))
            return AccountOperationResult.UserNameTaken;

        newUserName = wanted;
        return AccountOperationResult.Success;
    }

    /// <summary>
    /// Whether an account other than <paramref name="ignoring"/> already signs in with this name.
    /// </summary>
    /// <remarks>
    /// Case-INSENSITIVE, matching how <see cref="FindRecord"/> and sign-in resolve a name: "Tina"
    /// and "tina" must not both exist, or one of them can never be signed into.
    ///
    /// Public because the screens ask it while the user is still typing — telling somebody a name is
    /// taken after they have filled in the whole form is a worse answer than telling them at the
    /// keystroke. The create and rename paths re-check it themselves regardless; this is the
    /// courtesy, not the guard.
    /// </remarks>
    public bool IsUserNameTaken(string? userName, CredentialRecord? ignoring = null)
    {
        var wanted = (userName ?? string.Empty).Trim();

        if (wanted.Length == 0)
            return false;

        return _file.Users.Exists(candidate =>
            !ReferenceEquals(candidate, ignoring)
            && string.Equals(candidate.UserName, wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Whether a name is free for an account OTHER than the one signing in as it now.</summary>
    public bool IsUserNameTakenByAnother(string? userName, string currentUserName)
        => IsUserNameTaken(userName, FindRecord(currentUserName));

    /// <summary>
    /// Renames an account.
    /// </summary>
    /// <remarks>
    /// <see cref="CredentialFile.ProvisionedAccounts"/> is deliberately NOT touched. It records
    /// which SEED names this installation has already created, so that deleting a seeded account
    /// sticks — and <see cref="ProvisionSeedAccounts"/> looks each seed name up in it directly.
    /// Renaming the entry from "staff" to "sam" would leave "staff" unlisted, and the next load
    /// would seed a brand-new "staff" with a known password beside the renamed original. The old
    /// name staying put is exactly what prevents that.
    ///
    /// Memberships need no attention either: they key on <c>Shop.PublicId</c>, not on the login.
    /// </remarks>
    private static void ApplyRename(CredentialRecord record, string newUserName)
        => record.UserName = newUserName;

    /// <summary>
    /// Creates an account with no memberships. Deliberately no way to create an administrator: the
    /// <c>admin</c> account is the only one, by the product's rule.
    /// </summary>
    public AccountOperationResult CreateAccount(string userName, string password)
    {
        var name = (userName ?? string.Empty).Trim();

        if (string.IsNullOrEmpty(name))
            return AccountOperationResult.UserNameRequired;

        var rejection = CheckPassword(name, password);

        if (rejection != AccountOperationResult.Success)
            return rejection;

        if (FindRecord(name) is not null)
            return AccountOperationResult.UserNameTaken;

        _file.Users.Add(CreateRecord(name, password, isAdministrator: false));
        Save(_file);
        return AccountOperationResult.Success;
    }

    /// <summary>
    /// Deletes an account. Administrators cannot be deleted — an installation with no administrator
    /// can never be administered again, and the file is only editable by hand.
    /// </summary>
    public AccountOperationResult DeleteAccount(string userName)
    {
        var record = FindRecord(userName);

        if (record is null)
            return AccountOperationResult.NotFound;

        if (record.IsAdministrator)
            return AccountOperationResult.Protected;

        // Deleting the account you are signed in as would leave a session whose credentials no
        // longer exist, with capabilities nobody can revoke short of a restart.
        if (IsCurrentUser(record.UserName))
            return AccountOperationResult.Protected;

        _file.Users.Remove(record);
        Save(_file);
        return AccountOperationResult.Success;
    }

    /// <summary>
    /// Sets which roles an account holds in each of the given shops, leaving every other property of
    /// those memberships — activation, join date, shift — untouched. An empty role list removes the
    /// membership; a shop absent from the dictionary is not considered at all.
    /// </summary>
    public AccountOperationResult SetShopRoles(
        string userName, IReadOnlyDictionary<Guid, IReadOnlyList<string>> rolesByShop)
    {
        ArgumentNullException.ThrowIfNull(rolesByShop);

        var record = FindRecord(userName);

        if (record is null)
            return AccountOperationResult.NotFound;

        // An administrator already has every role in every shop; storing memberships for them would
        // be a second, contradictable source of truth for the same answer.
        if (record.IsAdministrator)
            return AccountOperationResult.Protected;

        foreach (var (shopPublicId, roles) in rolesByShop)
            ApplyShopRoles(record, shopPublicId, roles);

        Save(_file);
        RefreshCurrentUser(record);
        return AccountOperationResult.Success;
    }

    private static void ApplyShopRoles(
        CredentialRecord record, Guid shopPublicId, IReadOnlyList<string> roleIds)
    {
        var existing = record.Memberships.Find(m => m.ShopPublicId == shopPublicId);

        if (roleIds.Count == 0)
        {
            if (existing is not null)
                record.Memberships.Remove(existing);

            return;
        }

        var ordered = NormalizeRoleIds(roleIds);

        if (existing is null)
        {
            record.Memberships.Add(new ShopMembership
            {
                ShopPublicId = shopPublicId,
                RoleIds = ordered,
                JoinedOn = DateTime.Today
            });
            return;
        }

        existing.RoleIds = ordered;
    }

    /// <summary>
    /// Role ids as they are stored: de-duplicated case-insensitively and in catalog order, so two
    /// memberships holding the same roles are written the same way and diff as unchanged.
    /// </summary>
    private static List<string> NormalizeRoleIds(IEnumerable<string> roleIds)
    {
        var wanted = new HashSet<string>(roleIds, StringComparer.OrdinalIgnoreCase);

        return RolePermissionStore.Instance.All()
            .Select(role => role.Id)
            .Where(id => wanted.Contains(id))
            .ToList();
    }

    /// <summary>
    /// How many memberships name a role. What the permission panel tells the administrator before
    /// they delete one — "this removes it from four people" is a different decision from "nobody
    /// holds this".
    /// </summary>
    public int HoldersOf(string roleId)
        => _file.Users.Count(record => record.Memberships.Exists(membership =>
            membership.RoleIds.Contains(roleId, StringComparer.OrdinalIgnoreCase)));

    /// <summary>
    /// Withdraws a role from everybody who holds it. Called by <c>RolePermissionStore.Delete</c> as
    /// part of the same operation, so the catalog and the memberships cannot disagree about which
    /// roles exist.
    /// </summary>
    /// <remarks>
    /// A membership left with NO roles is deliberately kept rather than removed. It renders as "no
    /// role" on the roster, which is a fact somebody can see and fix; deleting it would quietly
    /// delist a person from a shop as a side effect of tidying up a role list, and nothing on any
    /// screen would say that had happened.
    /// </remarks>
    public void DropRole(string roleId)
    {
        var changed = false;

        foreach (var membership in _file.Users.SelectMany(record => record.Memberships))
            changed |= membership.RoleIds.RemoveAll(id => IdMatches(id, roleId)) > 0;

        if (!changed)
            return;

        Save(_file);

        if (CurrentUser is not null && FindRecord(CurrentUser.UserName) is { } current)
            RefreshCurrentUser(current);
    }

    private static bool IdMatches(string? left, string? right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Second half of the upgrade from a single global role to per-shop memberships: an account that
    /// held Manager or Staff before could open every shop, so it is made an active member of every
    /// shop that exists, with that role.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT part of <see cref="LoadOrSeed"/>: this service is constructed for the login
    /// window, which runs before the generic host is built and therefore before any shop can be
    /// read. Call it once the database is ready — <c>App.StartApplicationAsync</c> does, right after
    /// the shop bootstrap — and before the first shop list is shown, or the migrated users would be
    /// offered nothing to open.
    /// </remarks>
    public void ApplyLegacyShopMemberships(IEnumerable<Guid> shopPublicIds)
    {
        ArgumentNullException.ThrowIfNull(shopPublicIds);

        if (_file.SchemaVersion >= CurrentSchemaVersion)
            return;

        var shops = shopPublicIds.Distinct().ToList();

        foreach (var record in _file.Users)
        {
            if (record.LegacyRole is { } legacy && !record.IsAdministrator)
            {
                foreach (var shopId in shops)
                    ApplyShopRoles(record, shopId, new[] { LegacyRoleIds.For(legacy) });
            }

            record.LegacyRole = null;
        }

        _file.SchemaVersion = CurrentSchemaVersion;
        Save(_file);

        // The user signed in BEFORE this ran, so their session snapshot predates the memberships
        // they were just granted. Without this they would sign in and be told no shop is available.
        if (CurrentUser is not null && FindRecord(CurrentUser.UserName) is { } current)
            RefreshCurrentUser(current);
    }
}
