using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CameywareOrder.Models;
using Path = System.IO.Path;
using CameywareOrder.Configuration;

namespace CameywareOrder.Services;

/// <summary>
/// Sign-in and authorization for the application. Accounts live in <c>credentials.json</c> under the
/// app's local AppData folder, seeded with a single <c>admin</c> / <c>admin</c> administrator on
/// first run.
///
/// Deliberately file-backed rather than a database table, for two reasons: a corrupt or locked
/// database still lets you reach the login screen, and — more importantly — accounts are NOT wiped
/// by 本地配置 → 导入 → 数据库, which replaces the whole database file wholesale.
///
/// AUTHORIZATION IS PER SHOP. An account is either an administrator (everything, everywhere) or it
/// holds a <see cref="ShopMembership"/> per shop it belongs to — the role(s) it has there, whether
/// it is still active, when it joined, and its shift. That makes "manager in one branch, staff in
/// another, and suspended in a third" a data question rather than a code question, and it means the
/// answer to "what may this user do" always needs a shop to be asked about. <see cref="BindShop"/>
/// supplies it, so the capability properties can stay simple bindings for the UI.
///
/// SCOPE: this is an access gate, not a security boundary. Any local user can delete or edit the
/// file, which is also the only password-reset path. Passwords are hashed so they are not readable
/// at rest and are not exposed if the file is shared, but nothing here withstands someone with
/// write access to the machine. Do not build a feature that assumes otherwise.
/// </summary>
public sealed class AuthenticationService
{
    private const string FileName = "credentials.json";

    /// <summary>
    /// Shape of <c>credentials.json</c>. 1 = a single global <c>Role</c> per account; 2 = an
    /// administrator flag plus flat (shop, role) assignments; 3 = one <see cref="ShopMembership"/>
    /// per shop, carrying activation, join date and shift alongside the roles. A file below this
    /// version is upgraded in two steps — see <see cref="ApplyLegacyShopMemberships"/> for why the
    /// second one cannot run here.
    /// </summary>
    private const int CurrentSchemaVersion = 3;

    /// <summary>
    /// The account that must always exist. Every other account can be deleted; deleting this one
    /// would leave an installation nobody can administer, so it is topped up on every load.
    /// </summary>
    private const string AdministratorUserName = "admin";

    /// <summary>
    /// Accounts created ONCE, on the first load that does not already record them as provisioned.
    /// Their password matches their user name.
    ///
    /// "Once" is the whole point: topping these up on every load — which is what this did before
    /// accounts could be managed in the UI — would resurrect an account the administrator had
    /// deliberately deleted. <see cref="CredentialFile.ProvisionedAccounts"/> is the record that
    /// makes a deletion stick. <see cref="AdministratorUserName"/> is deliberately exempt.
    ///
    /// test1 / test2 are created with NO roles at all, so a fresh installation has two accounts to
    /// exercise the assignment screen with.
    /// </summary>
    private static readonly (string UserName, string Password, bool IsAdministrator)[] SeedAccounts =
    {
        (AdministratorUserName, "admin", true),
        ("manager", "manager", false),
        ("staff", "staff", false),
        ("test1", "test1", false),
        ("test2", "test2", false)
    };

    // PBKDF2-HMAC-SHA256. Stored per record so the cost can be raised later without invalidating
    // existing accounts.
    private const int DefaultIterations = 100_000;
    private const int SaltByteCount = 16;
    private const int HashByteCount = 32;

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    // DECLARED LAST ON PURPOSE. Static field initializers run in textual order, and constructing
    // the singleton reads SeedAccounts and SerializerOptions — so declaring it above them left
    // both null during construction and the type initializer threw
    // TypeInitializationException wrapping a NullReferenceException, which surfaced as every
    // sign-in failing regardless of the credentials entered. Keep this below everything it uses.
    public static AuthenticationService Instance { get; } = new();

    private readonly CredentialFile _file;
    private Guid? _activeShopPublicId;

    private AuthenticationService()
    {
        _file = LoadOrSeed();
    }

    /// <summary>Raised after the signed-in user or the shop their capabilities resolve against changed.</summary>
    public event EventHandler? CapabilitiesChanged;

    /// <summary>The account that signed in this session, or null before a successful sign-in.</summary>
    public UserAccount? CurrentUser { get; private set; }

    /// <summary>Full access to everything, in every shop.</summary>
    public bool IsAdministrator => CurrentUser?.IsAdministrator ?? false;

    /// <summary>
    /// The signed-in user's strongest role in the shop currently open, or null when no shop is open
    /// or they hold no active role in it. An administrator reports <see cref="UserRole.Admin"/>
    /// everywhere.
    /// </summary>
    public UserRole? CurrentRole => _activeShopPublicId is { } shopId ? RoleFor(shopId) : AdministratorRole();

    /// <summary>
    /// Named capabilities rather than role comparisons spread through the UI — when the rules grow,
    /// only these change.
    /// </summary>
    /// <remarks>
    /// Creating a shop, moving data in and out of the installation, and managing accounts across all
    /// shops are administrator work: they act on the installation as a whole rather than on one
    /// branch, so they cannot be delegated to a role that only exists inside a single shop.
    /// </remarks>
    public bool CanCreateShops => IsAdministrator;

    /// <summary>Managing every account in the installation and its shop memberships.</summary>
    public bool CanManageUsers => IsAdministrator;

    /// <summary>
    /// The 本地数据库 and 导入/导出 menus, and the database path in the status bar. These read and
    /// replace the whole installation's data, which is not a per-shop action.
    /// </summary>
    public bool CanUseDataTools => IsAdministrator;

    /// <summary>
    /// Whether the user may change how the OPEN shop is configured — its settings, currency,
    /// measurement terms and receipt branding. A manager runs their branch; staff take orders in it.
    /// </summary>
    public bool CanConfigureShop => IsAdministrator || CurrentRole == UserRole.Manager;

    /// <summary>
    /// Whether the user may manage the OPEN shop's roster: who works there, their role, their shift
    /// and whether they are still active.
    /// </summary>
    /// <remarks>
    /// Same holders as <see cref="CanConfigureShop"/> today, but a separate name on purpose — "who
    /// works here" and "how this shop prices and prints" are different decisions, and the first is
    /// the one most likely to be delegated further later.
    /// </remarks>
    public bool CanManageStoreMembers => IsAdministrator || CurrentRole == UserRole.Manager;

    /// <summary>
    /// Whether the user may delete an account outright, as opposed to deactivating a membership.
    /// Deletion is installation-wide — the person may work in branches this user has never seen.
    /// </summary>
    public bool CanDeleteAccounts => IsAdministrator;

    /// <summary>
    /// Whether the user may run the application in a language of their choosing. Only an
    /// administrator can; everyone else follows the language the shop is configured for, so a
    /// branch's staff all see the same thing. (The login screen itself stays switchable for
    /// everyone — otherwise a user could not read the screen they sign in on.)
    /// </summary>
    public bool CanChooseLanguage => IsAdministrator;

    /// <summary>
    /// Points the capability properties at a shop. Called from <c>App.ApplyActiveShop</c> BEFORE the
    /// shop is published to <see cref="ShopContext"/>, so anything reacting to that change already
    /// sees the new answers.
    /// </summary>
    public void BindShop(Shop? shop)
    {
        _activeShopPublicId = shop?.PublicId;
        CapabilitiesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Verifies a credential and, on success, records it as the signed-in user.
    /// </summary>
    /// <remarks>
    /// An unknown user name and a wrong password report the SAME failure, or the dialog becomes a
    /// user-name oracle. A deactivated account is reported distinctly: the credential was right and
    /// the person needs to be told to talk to their manager rather than to keep retyping.
    /// </remarks>
    public SignInResult Authenticate(string userName, string password)
    {
        var record = FindRecord(userName);

        if (record is null)
        {
            // Hash anyway so an unknown user name costs the same time as a wrong password, and the
            // response time cannot be used to enumerate accounts.
            _ = DeriveHash(password, RandomNumberGenerator.GetBytes(SaltByteCount), DefaultIterations);
            return SignInResult.Failed(SignInFailure.InvalidCredentials);
        }

        if (!Verify(password, record))
            return SignInResult.Failed(SignInFailure.InvalidCredentials);

        if (IsLockedOut(record))
            return SignInResult.Failed(SignInFailure.Deactivated);

        CurrentUser = ToAccount(record);
        CapabilitiesChanged?.Invoke(this, EventArgs.Empty);
        return SignInResult.Succeeded(CurrentUser);
    }

    /// <summary>
    /// Whether a valid credential should still be refused. Only when the account belongs to at least
    /// one shop and EVERY one of those memberships has been deactivated — being suspended in one
    /// branch must not cost someone their job in another.
    /// </summary>
    /// <remarks>
    /// An account with no memberships at all is NOT locked out: that is a new hire who has not been
    /// posted to a branch yet. They sign in and are told no shop is available, which is a different
    /// and more accurate thing to say than "your account is deactivated".
    /// </remarks>
    private static bool IsLockedOut(CredentialRecord record)
        => !record.IsAdministrator
            && record.Memberships.Count > 0
            && record.Memberships.TrueForAll(membership => !membership.IsActive);

    /// <summary>
    /// Ends the session. Every capability gate reads <see cref="CurrentUser"/>, so clearing it
    /// immediately revokes them — which is why the caller must take down the main window before
    /// calling this, not after.
    /// </summary>
    public void SignOut()
    {
        CurrentUser = null;
        _activeShopPublicId = null;
        CapabilitiesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The signed-in user's strongest role in a given shop, or null when they hold none there or
    /// their membership has been deactivated.
    /// </summary>
    public UserRole? RoleFor(Guid shopPublicId)
    {
        if (CurrentUser is null)
            return null;

        if (CurrentUser.IsAdministrator)
            return UserRole.Admin;

        var membership = CurrentUser.Memberships
            .FirstOrDefault(candidate => candidate.ShopPublicId == shopPublicId);

        return membership is { IsActive: true } ? StrongestRole(membership.Roles) : null;
    }

    /// <summary>Whether the signed-in user may open a given shop at all.</summary>
    public bool CanAccessShop(Guid shopPublicId) => RoleFor(shopPublicId) is not null;

    /// <summary>Filters a shop list down to the ones the signed-in user may open.</summary>
    public List<Shop> FilterAccessibleShops(IEnumerable<Shop> shops)
    {
        ArgumentNullException.ThrowIfNull(shops);
        return shops.Where(shop => CanAccessShop(shop.PublicId)).ToList();
    }

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
                entry.Record.DisplayName,
                entry.Record.BirthDate,
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

        if (string.IsNullOrEmpty(password))
            return AccountOperationResult.PasswordRequired;

        if (profile.Roles.Count == 0)
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

        if (profile.Roles.Count == 0)
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
        record.DisplayName = string.IsNullOrWhiteSpace(profile.DisplayName) ? null : profile.DisplayName.Trim();
        record.BirthDate = profile.BirthDate;

        var membership = record.Memberships.First(candidate => candidate.ShopPublicId == shopPublicId);

        if (membership.IsActive && !profile.IsActive)
            membership.DeactivatedOn = DateTime.Now;
        else if (!membership.IsActive && profile.IsActive)
            membership.DeactivatedOn = null;

        membership.IsActive = profile.IsActive;
        membership.Roles = profile.Roles.Distinct().OrderBy(role => role).ToList();
        membership.JoinedOn = profile.JoinedOn;
        membership.ShiftStart = profile.ShiftStart;
        membership.ShiftEnd = profile.ShiftEnd;
    }

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

        var managedShops = CurrentUser.Memberships
            .Where(membership => membership.IsActive && membership.Roles.Contains(UserRole.Manager))
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
    /// Creates an account with no memberships. Deliberately no way to create an administrator: the
    /// <c>admin</c> account is the only one, by the product's rule.
    /// </summary>
    public AccountOperationResult CreateAccount(string userName, string password)
    {
        var name = (userName ?? string.Empty).Trim();

        if (string.IsNullOrEmpty(name))
            return AccountOperationResult.UserNameRequired;

        if (string.IsNullOrEmpty(password))
            return AccountOperationResult.PasswordRequired;

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

    /// <summary>Replaces an account's password. Allowed for every account, including the administrator.</summary>
    public AccountOperationResult SetPassword(string userName, string password)
    {
        var record = FindRecord(userName);

        if (record is null)
            return AccountOperationResult.NotFound;

        if (string.IsNullOrEmpty(password))
            return AccountOperationResult.PasswordRequired;

        var salt = RandomNumberGenerator.GetBytes(SaltByteCount);
        record.Iterations = DefaultIterations;
        record.Salt = Convert.ToBase64String(salt);
        record.Hash = Convert.ToBase64String(DeriveHash(password, salt, DefaultIterations));
        record.MustChangePassword = false;

        Save(_file);
        RefreshCurrentUser(record);
        return AccountOperationResult.Success;
    }

    /// <summary>
    /// Sets which roles an account holds in each of the given shops, leaving every other property of
    /// those memberships — activation, join date, shift — untouched. An empty role list removes the
    /// membership; a shop absent from the dictionary is not considered at all.
    /// </summary>
    public AccountOperationResult SetShopRoles(
        string userName, IReadOnlyDictionary<Guid, IReadOnlyList<UserRole>> rolesByShop)
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
        CredentialRecord record, Guid shopPublicId, IReadOnlyList<UserRole> roles)
    {
        var existing = record.Memberships.Find(m => m.ShopPublicId == shopPublicId);

        if (roles.Count == 0)
        {
            if (existing is not null)
                record.Memberships.Remove(existing);

            return;
        }

        var ordered = roles.Distinct().OrderBy(role => role).ToList();

        if (existing is null)
        {
            record.Memberships.Add(new ShopMembership
            {
                ShopPublicId = shopPublicId,
                Roles = ordered,
                JoinedOn = DateTime.Today
            });
            return;
        }

        existing.Roles = ordered;
    }

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
                    ApplyShopRoles(record, shopId, new[] { legacy });
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

    // --- Internals ------------------------------------------------------------------------------

    private UserRole? AdministratorRole() => IsAdministrator ? UserRole.Admin : null;

    private bool IsCurrentUser(string userName)
        => CurrentUser is not null
            && string.Equals(CurrentUser.UserName, userName, StringComparison.OrdinalIgnoreCase);

    private void RefreshCurrentUser(CredentialRecord record)
    {
        if (!IsCurrentUser(record.UserName))
            return;

        CurrentUser = ToAccount(record);
        CapabilitiesChanged?.Invoke(this, EventArgs.Empty);
    }

    private CredentialRecord? FindRecord(string userName)
        => _file.Users.Find(user =>
            string.Equals(user.UserName, userName, StringComparison.OrdinalIgnoreCase));

    // Copied rather than handed out: the session snapshot and the screens must not be able to edit
    // the file's in-memory state behind Save's back.
    private static UserAccount ToAccount(CredentialRecord record)
        => new(record.UserName, record.DisplayName, record.IsAdministrator,
            record.Memberships.Select(Clone).ToList());

    private static ShopMembership Clone(ShopMembership membership) => new()
    {
        ShopPublicId = membership.ShopPublicId,
        Roles = new List<UserRole>(membership.Roles),
        IsActive = membership.IsActive,
        JoinedOn = membership.JoinedOn,
        DeactivatedOn = membership.DeactivatedOn,
        ShiftStart = membership.ShiftStart,
        ShiftEnd = membership.ShiftEnd
    };

    /// <summary>
    /// The strongest of a set of roles. <see cref="UserRole"/> is ordered strongest-first
    /// (Admin 0, Manager 1, Staff 2), so "strongest" is the minimum — which is what makes holding
    /// both Manager and Staff in the same shop behave as Manager rather than as an ambiguity.
    /// </summary>
    private static UserRole? StrongestRole(IEnumerable<UserRole> roles)
        // Projected to the NULLABLE enum so an empty set yields null; Min over the non-nullable
        // enum throws on an empty sequence.
        => roles.Cast<UserRole?>().Min();

    private static bool Verify(string password, CredentialRecord record)
    {
        byte[] expected;
        byte[] salt;
        try
        {
            expected = Convert.FromBase64String(record.Hash);
            salt = Convert.FromBase64String(record.Salt);
        }
        catch (FormatException)
        {
            return false; // corrupt record; treat as a failed sign-in rather than crashing
        }

        var actual = DeriveHash(password, salt, record.Iterations);

        // Constant-time: a byte-by-byte comparison leaks how much of the hash matched.
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] DeriveHash(string password, byte[] salt, int iterations)
        => Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password ?? string.Empty),
            salt,
            iterations <= 0 ? DefaultIterations : iterations,
            HashAlgorithmName.SHA256,
            HashByteCount);

    private static CredentialRecord CreateRecord(string userName, string password, bool isAdministrator)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltByteCount);
        return new CredentialRecord
        {
            UserName = userName,
            IsAdministrator = isAdministrator,
            Iterations = DefaultIterations,
            Salt = Convert.ToBase64String(salt),
            Hash = Convert.ToBase64String(DeriveHash(password, salt, DefaultIterations)),
            MustChangePassword = true,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Resolved through <see cref="UserDataPaths.ResolveConfigFile"/>, which moves the file out of
    /// the flat data-folder root into Config/ the first time — and returns the OLD path if it
    /// cannot, so a failed tidy-up can never make credentials unreadable.
    /// </summary>
    private static string SettingFilePath => UserDataPaths.ResolveConfigFile(FileName);

    private static string SettingDirectory => Path.GetDirectoryName(SettingFilePath)!;

    private static CredentialFile LoadOrSeed()
    {
        // A missing or corrupt file starts empty rather than throwing: deleting it is the only
        // password-reset path, and it must not lock the shop out of its own application.
        var existing = TryLoad();
        var file = existing ?? new CredentialFile { SchemaVersion = CurrentSchemaVersion };

        var changed = existing is null;
        changed |= UpgradeAccountShape(file);
        changed |= ProvisionSeedAccounts(file);

        if (changed)
            Save(file);

        return file;
    }

    /// <summary>
    /// Every upgrade step that needs no shop list: a global <c>Role = Admin</c> becomes the
    /// administrator flag, and flat version-2 assignments fold into one membership per shop. A
    /// non-admin legacy role is left in place for <see cref="ApplyLegacyShopMemberships"/>, which is
    /// also why the version is only bumped when nothing is left waiting for it.
    /// </summary>
    private static bool UpgradeAccountShape(CredentialFile file)
    {
        if (file.SchemaVersion >= CurrentSchemaVersion)
            return false;

        foreach (var record in file.Users.Where(record => record.LegacyRole == UserRole.Admin))
        {
            record.IsAdministrator = true;
            record.LegacyRole = null;
        }

        foreach (var record in file.Users.Where(record => record.LegacyAssignments is { Count: > 0 }))
            FoldAssignmentsIntoMemberships(record);

        foreach (var record in file.Users)
            record.LegacyAssignments = null;

        // A version-1 file predates the provisioning record, so everything it already holds counts
        // as provisioned. Without this, seeding would re-add an account the file shows was deleted.
        foreach (var name in file.Users
                     .Select(record => record.UserName)
                     .Where(name => !file.ProvisionedAccounts.Contains(name, StringComparer.OrdinalIgnoreCase)))
        {
            file.ProvisionedAccounts.Add(name);
        }

        // Only once no record still needs a shop list, which this method cannot obtain.
        if (file.Users.TrueForAll(record => record.LegacyRole is null))
            file.SchemaVersion = CurrentSchemaVersion;

        return true;
    }

    private static void FoldAssignmentsIntoMemberships(CredentialRecord record)
    {
        var grouped = record.LegacyAssignments!
            .GroupBy(assignment => assignment.ShopPublicId)
            .Select(group => new ShopMembership
            {
                ShopPublicId = group.Key,
                Roles = group.Select(assignment => assignment.Role).Distinct().OrderBy(role => role).ToList()
                // IsActive defaults to true: an assignment that existed was an assignment in force.
            });

        foreach (var membership in grouped.Where(candidate =>
                     record.Memberships.TrueForAll(existing => existing.ShopPublicId != candidate.ShopPublicId)))
        {
            record.Memberships.Add(membership);
        }
    }

    private static bool ProvisionSeedAccounts(CredentialFile file)
    {
        var added = false;

        foreach (var (userName, password, isAdministrator) in SeedAccounts)
        {
            var exists = file.Users.Exists(user =>
                string.Equals(user.UserName, userName, StringComparison.OrdinalIgnoreCase));

            if (exists)
                continue;

            // The administrator is restored unconditionally; everything else is created once and
            // stays deleted if an administrator deletes it.
            var isProtectedAccount = string.Equals(userName, AdministratorUserName, StringComparison.OrdinalIgnoreCase);
            if (!isProtectedAccount
                && file.ProvisionedAccounts.Contains(userName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            file.Users.Add(CreateRecord(userName, password, isAdministrator));

            if (!file.ProvisionedAccounts.Contains(userName, StringComparer.OrdinalIgnoreCase))
                file.ProvisionedAccounts.Add(userName);

            added = true;
        }

        return added;
    }

    private static CredentialFile? TryLoad()
    {
        try
        {
            if (!File.Exists(SettingFilePath))
                return null;

            return JsonSerializer.Deserialize<CredentialFile>(File.ReadAllText(SettingFilePath));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable file re-seeds the default admin rather than locking the shop
            // out of its own application.
            return null;
        }
    }

    private static void Save(CredentialFile file)
    {
        try
        {
            Directory.CreateDirectory(SettingDirectory);
            File.WriteAllText(SettingFilePath, JsonSerializer.Serialize(file, SerializerOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Non-fatal, matching the other stores: the in-memory account still works this session.
        }
    }
}

/// <summary>Why a sign-in was refused.</summary>
public enum SignInFailure
{
    None,

    /// <summary>Unknown user name OR wrong password — never distinguished, by design.</summary>
    InvalidCredentials,

    /// <summary>The credential was correct, but every shop this account belongs to has deactivated it.</summary>
    Deactivated
}

/// <summary>Outcome of a sign-in attempt.</summary>
public readonly record struct SignInResult(UserAccount? User, SignInFailure Failure)
{
    public static SignInResult Succeeded(UserAccount user) => new(user, SignInFailure.None);

    public static SignInResult Failed(SignInFailure failure) => new(null, failure);
}

/// <summary>Outcome of an account edit, so the caller can render its own localized message.</summary>
public enum AccountOperationResult
{
    Success,
    UserNameRequired,
    UserNameTaken,
    PasswordRequired,
    NotFound,

    /// <summary>A member must hold at least one role in the shop, or they are not a member of it.</summary>
    RoleRequired,

    /// <summary>The account or the change is not editable — an administrator, or your own account.</summary>
    Protected
}

/// <summary>The signed-in user, as the rest of the app sees them.</summary>
public sealed record UserAccount(
    string UserName,
    string? DisplayName,
    bool IsAdministrator,
    IReadOnlyList<ShopMembership> Memberships);

/// <summary>One person's place in one shop, as the roster screen edits it.</summary>
public sealed record StoreMember(
    string UserName,
    string? DisplayName,
    DateTime? BirthDate,
    bool IsAdministrator,
    ShopMembership Membership)
{
    /// <summary>What to call this person on screen — their name if they have one, else the account.</summary>
    public string DisplayLabel => string.IsNullOrWhiteSpace(DisplayName) ? UserName : DisplayName;
}

/// <summary>
/// The editable half of a member: the account-level profile plus their standing in ONE shop.
/// Grouped into one object rather than passed as eight parameters.
/// </summary>
public sealed record MemberProfile(
    string? DisplayName,
    DateTime? BirthDate,
    IReadOnlyList<UserRole> Roles,
    bool IsActive,
    DateTime? JoinedOn,
    TimeOnly? ShiftStart,
    TimeOnly? ShiftEnd);

/// <summary>
/// One person's membership of one shop: the role(s) they hold there, whether they still work there,
/// when they started, when they were delisted, and the shift they work.
/// </summary>
/// <remarks>
/// Keyed on <see cref="Shop.PublicId"/> rather than <see cref="Shop.Id"/> for the reason documented
/// on that property: this file lives OUTSIDE the database, and whole databases move between
/// machines, where the local autoincrement ids collide.
///
/// <see cref="Roles"/> is a SET because holding both Manager and Staff in one shop is legal; it
/// resolves to Manager. Activation lives here rather than on the account because suspending someone
/// at one branch must not cost them their job at another.
/// </remarks>
public sealed class ShopMembership
{
    public Guid ShopPublicId { get; set; }

    public List<UserRole> Roles { get; set; } = new();

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

    /// <summary>The person's name, as the roster shows it. Null falls back to the user name.</summary>
    public string? DisplayName { get; set; }

    public DateTime? BirthDate { get; set; }

    /// <summary>Full access everywhere. Held by exactly one account, which cannot be deleted.</summary>
    public bool IsAdministrator { get; set; }

    /// <summary>The shops this account belongs to, and its standing in each.</summary>
    public List<ShopMembership> Memberships { get; set; } = new();

    public int Iterations { get; set; }
    public string Salt { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;

    /// <summary>Reserved: seeded true for new accounts, not yet enforced anywhere.</summary>
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
}

/// <summary>One (shop, role) pair as schema version 2 stored it. Read-only history; do not extend.</summary>
public sealed class LegacyShopAssignment
{
    public Guid ShopPublicId { get; set; }

    public UserRole Role { get; set; }
}
