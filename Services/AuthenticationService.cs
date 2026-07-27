using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CameywareOrder.Models;
using Path = System.IO.Path;

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
/// holds a set of <see cref="ShopAssignment"/>s — one or more roles in each shop it may open. That
/// makes "manager in one branch, staff in another, and both in a third" a data question rather than
/// a code question, and it means the answer to "what may this user do" always needs a shop to be
/// asked about. <see cref="BindShop"/> supplies it, so the capability properties can stay simple
/// bindings for the UI.
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
    /// administrator flag plus per-shop assignments. A file below this version is upgraded in two
    /// steps — see <see cref="ApplyLegacyShopAssignments"/> for why the second one cannot run here.
    /// </summary>
    private const int CurrentSchemaVersion = 2;

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
    /// or they hold no role in it. An administrator reports <see cref="UserRole.Admin"/> everywhere.
    /// </summary>
    public UserRole? CurrentRole => _activeShopPublicId is { } shopId ? RoleFor(shopId) : AdministratorRole();

    /// <summary>
    /// Named capabilities rather than role comparisons spread through the UI — when the rules grow,
    /// only these change.
    /// </summary>
    /// <remarks>
    /// Creating a shop, moving data in and out of the installation, and managing accounts are all
    /// administrator work: they act on the installation as a whole rather than on one branch, so
    /// they cannot be delegated to a role that only exists inside a single shop.
    /// </remarks>
    public bool CanCreateShops => IsAdministrator;

    /// <summary>Managing accounts and their shop assignments.</summary>
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
    /// Verifies a credential and, on success, records it as the signed-in user. Returns null when
    /// the user name is unknown or the password does not match — the caller must not be told which,
    /// or the dialog becomes a user-name oracle.
    /// </summary>
    public UserAccount? Authenticate(string userName, string password)
    {
        var record = FindRecord(userName);

        if (record is null)
        {
            // Hash anyway so an unknown user name costs the same time as a wrong password, and the
            // response time cannot be used to enumerate accounts.
            _ = DeriveHash(password, RandomNumberGenerator.GetBytes(SaltByteCount), DefaultIterations);
            return null;
        }

        if (!Verify(password, record))
            return null;

        CurrentUser = ToAccount(record);
        CapabilitiesChanged?.Invoke(this, EventArgs.Empty);
        return CurrentUser;
    }

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
    /// The signed-in user's strongest role in a given shop, or null when they hold none there.
    /// </summary>
    public UserRole? RoleFor(Guid shopPublicId)
    {
        if (CurrentUser is null)
            return null;

        if (CurrentUser.IsAdministrator)
            return UserRole.Admin;

        return StrongestRole(CurrentUser.Assignments, shopPublicId);
    }

    /// <summary>Whether the signed-in user may open a given shop at all.</summary>
    public bool CanAccessShop(Guid shopPublicId) => RoleFor(shopPublicId) is not null;

    /// <summary>Filters a shop list down to the ones the signed-in user may open.</summary>
    public List<Shop> FilterAccessibleShops(IEnumerable<Shop> shops)
    {
        ArgumentNullException.ThrowIfNull(shops);
        return shops.Where(shop => CanAccessShop(shop.PublicId)).ToList();
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
    /// Creates an account with no roles. Deliberately no way to create an administrator: the
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
    /// Replaces an account's shop assignments wholesale. Passing an empty set is how an account is
    /// left with no access at all, which is a legitimate state — a new hire exists before they are
    /// posted to a branch.
    /// </summary>
    public AccountOperationResult SetAssignments(string userName, IEnumerable<ShopAssignment> assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);

        var record = FindRecord(userName);

        if (record is null)
            return AccountOperationResult.NotFound;

        // An administrator already has every role in every shop; storing assignments for them would
        // be a second, contradictable source of truth for the same answer.
        if (record.IsAdministrator)
            return AccountOperationResult.Protected;

        record.Assignments = assignments
            .DistinctBy(assignment => (assignment.ShopPublicId, assignment.Role))
            .OrderBy(assignment => assignment.Role)
            .ToList();

        Save(_file);
        RefreshCurrentUser(record);
        return AccountOperationResult.Success;
    }

    /// <summary>
    /// Second half of the upgrade from a single global role to per-shop assignments: an account that
    /// held Manager or Staff before could open every shop, so it is granted that role in every shop
    /// that exists at migration time.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT part of <see cref="LoadOrSeed"/>: this service is constructed for the login
    /// window, which runs before the generic host is built and therefore before any shop can be
    /// read. Call it once the database is ready — <c>App.StartApplicationAsync</c> does, right after
    /// the shop bootstrap — and before the first shop list is shown, or the migrated users would be
    /// offered nothing to open.
    /// </remarks>
    public void ApplyLegacyShopAssignments(IEnumerable<Guid> shopPublicIds)
    {
        ArgumentNullException.ThrowIfNull(shopPublicIds);

        if (_file.SchemaVersion >= CurrentSchemaVersion)
            return;

        var shops = shopPublicIds.Distinct().ToList();

        foreach (var record in _file.Users)
        {
            if (record.LegacyRole is { } legacy && !record.IsAdministrator)
            {
                foreach (var shopId in shops.Where(shopId => !record.Assignments.Any(a => a.ShopPublicId == shopId && a.Role == legacy)))
                    record.Assignments.Add(new ShopAssignment { ShopPublicId = shopId, Role = legacy });
            }

            record.LegacyRole = null;
        }

        _file.SchemaVersion = CurrentSchemaVersion;
        Save(_file);

        // The user signed in BEFORE this ran, so their session snapshot predates the assignments
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
        => _file.Users.FirstOrDefault(user =>
            string.Equals(user.UserName, userName, StringComparison.OrdinalIgnoreCase));

    private static UserAccount ToAccount(CredentialRecord record)
        => new(record.UserName, record.IsAdministrator,
            record.Assignments.Select(a => new ShopAssignment { ShopPublicId = a.ShopPublicId, Role = a.Role }).ToList());

    /// <summary>
    /// The strongest role held in one shop. <see cref="UserRole"/> is ordered strongest-first
    /// (Admin 0, Manager 1, Staff 2), so "strongest" is the minimum — which is what makes holding
    /// both Manager and Staff in the same shop behave as Manager rather than as an ambiguity.
    /// </summary>
    private static UserRole? StrongestRole(IEnumerable<ShopAssignment> assignments, Guid shopPublicId)
        => assignments
            .Where(assignment => assignment.ShopPublicId == shopPublicId)
            .Select(assignment => assignment.Role)
            // Projected to the NULLABLE enum so an account with no role in this shop yields null;
            // Min over the non-nullable enum throws on an empty sequence.
            .Cast<UserRole?>()
            .Min();

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

    private static string SettingDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CameywareOrder");

    private static string SettingFilePath => Path.Combine(SettingDirectory, FileName);

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
    /// First half of the version-1 upgrade — the half that needs no shop list. A global
    /// <c>Role = Admin</c> becomes the administrator flag; every other legacy role is left in place
    /// for <see cref="ApplyLegacyShopAssignments"/> to turn into assignments.
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

        // A version-1 file predates the provisioning record, so everything it already holds counts
        // as provisioned. Without this, seeding would re-add an account the file shows was deleted.
        foreach (var name in file.Users
                     .Select(record => record.UserName)
                     .Where(name => !file.ProvisionedAccounts.Contains(name, StringComparer.OrdinalIgnoreCase)))
        {
            file.ProvisionedAccounts.Add(name);
        }

        return true;
    }

    private static bool ProvisionSeedAccounts(CredentialFile file)
    {
        var added = false;

        foreach (var (userName, password, isAdministrator) in SeedAccounts)
        {
            var exists = file.Users.Any(user =>
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

/// <summary>Outcome of an account edit, so the caller can render its own localized message.</summary>
public enum AccountOperationResult
{
    Success,
    UserNameRequired,
    UserNameTaken,
    PasswordRequired,
    NotFound,

    /// <summary>The account or the change is not editable — an administrator, or your own account.</summary>
    Protected
}

/// <summary>The signed-in user, as the rest of the app sees them.</summary>
public sealed record UserAccount(
    string UserName,
    bool IsAdministrator,
    IReadOnlyList<ShopAssignment> Assignments);

/// <summary>
/// One role an account holds in one shop. Keyed on <see cref="Shop.PublicId"/> rather than
/// <see cref="Shop.Id"/> for the reason documented on that property: this file lives OUTSIDE the
/// database, and whole databases move between machines, where the local autoincrement ids collide.
/// A pair may appear twice for the same shop with different roles — that is how "manager and staff
/// in the same branch" is stored.
/// </summary>
public sealed class ShopAssignment
{
    public Guid ShopPublicId { get; set; }

    public UserRole Role { get; set; }
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

    /// <summary>Full access everywhere. Held by exactly one account, which cannot be deleted.</summary>
    public bool IsAdministrator { get; set; }

    /// <summary>The shops this account may open, and with which role(s) in each.</summary>
    public List<ShopAssignment> Assignments { get; set; } = new();

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
}
