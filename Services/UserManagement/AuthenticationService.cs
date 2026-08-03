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
/// by Local Configuration → Import → Database, which replaces the whole database file wholesale.
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
public sealed partial class AuthenticationService
{
    private const string FileName = "credentials.json";

    /// <summary>
    /// Shape of <c>credentials.json</c>. 1 = a single global <c>Role</c> per account; 2 = an
    /// administrator flag plus flat (shop, role) assignments; 3 = one <see cref="ShopMembership"/>
    /// per shop, carrying activation, join date and shift alongside the roles; 4 = a person's name
    /// split into <c>FirstName</c> and <c>LastName</c>; 5 = memberships name roles by ID rather than
    /// by the fixed <see cref="UserRole"/> enum, so an installation can define roles of its own;
    /// 6 = <see cref="CredentialRecord.MustChangePassword"/> is enforced, and the upgrade arms it on
    /// every account still carrying the password this product once shipped it with. A file below
    /// this version is upgraded in two steps — see <see cref="ApplyLegacyShopMemberships"/> for why
    /// the second one cannot run here.
    /// </summary>
    private const int CurrentSchemaVersion = 6;

    /// <summary>
    /// The account that must always exist. Every other account can be deleted; deleting this one
    /// would leave an installation nobody can administer, so it is topped up on every load.
    /// </summary>
    private const string AdministratorUserName = "admin";

    /// <summary>
    /// The initial password of the one seeded account, valid for exactly one sign-in: the record is
    /// created with <see cref="CredentialRecord.MustChangePassword"/> set, so
    /// <see cref="Authenticate"/> refuses to open a session until it has been replaced.
    /// </summary>
    private const string AdministratorInitialPassword = "admin";



    /// <summary>
    /// The shortest password the application will store.
    /// </summary>
    /// <remarks>
    /// Public because the screens quote it: a rule the user is only told about by being refused is
    /// a rule they discover twice. The number appears on screen through
    /// <c>Users.Error.PasswordTooShort</c>, which is formatted with this constant rather than
    /// spelling it out in five languages — a translated "at least eight characters" is a lie the
    /// moment this line changes.
    /// </remarks>
    public const int MinimumPasswordLength = 8;

    // PBKDF2-HMAC-SHA256. Stored per record so the cost can be raised later without invalidating
    // existing accounts.
    private const int DefaultIterations = 100_000;
    private const int SaltByteCount = 16;
    private const int HashByteCount = 32;

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    /// <summary>The one instance. Constructed on first use, deliberately — see the remarks.</summary>
    /// <remarks>
    /// This used to be <c>{ get; } = new()</c>, declared last in the file with a comment saying to
    /// keep it below everything the constructor reads: static field initializers run in TEXTUAL
    /// order, and constructing the singleton reads <c>SeedAccounts</c> and
    /// <c>SerializerOptions</c>, so declaring it above them left both null and the type initializer
    /// threw — surfacing as every sign-in failing whatever credentials were typed.
    ///
    /// **Splitting this class into partials (v9.3.0) broke that rule the moment it was applied**,
    /// and it broke it invisibly: `SeedAccounts` moved to `AuthenticationService.Passwords.cs`, and
    /// across partial FILES there is no textual order to keep — the compiler picks one. The build
    /// stayed at 0/0 and the whole suite went red with a `TypeInitializationException`.
    ///
    /// A <see cref="Lazy{T}"/> removes the dependency rather than restating it: the constructor now
    /// runs on the first ACCESS, by which point every static field is initialized whatever order
    /// the compiler chose. Nothing about the singleton's lifetime changes — it is still built once,
    /// and <c>Lazy</c> is thread-safe by default.
    ///
    /// One consequence worth keeping in mind, since a harness depends on it: the credentials file is
    /// read on first access to <c>Instance</c> rather than on first touch of the TYPE.
    /// </remarks>
    public static AuthenticationService Instance => Singleton.Value;

    private static readonly Lazy<AuthenticationService> Singleton = new(() => new AuthenticationService());

    private readonly CredentialFile _file;
    private Guid? _activeShopPublicId;

    /// <summary>
    /// What the signed-in user may do right now, resolved once and re-resolved whenever anything it
    /// depends on moves — the session, the open shop, or the role catalog itself.
    /// </summary>
    /// <remarks>
    /// Cached rather than computed per question because the chrome asks a dozen of these every time a
    /// menu opens or a shop is switched, and each answer would otherwise walk every membership and
    /// every role. The cost of the cache is that every mutation has to refresh it — which is why the
    /// refresh and the change notification are the SAME call (<see cref="RefreshCapabilities"/>)
    /// rather than two things a new code path could do one of.
    /// </remarks>
    private IReadOnlySet<AppCapability> _capabilities = new HashSet<AppCapability>();

    private AuthenticationService()
    {
        _file = LoadOrSeed();

        // Editing a role changes what everyone holding it may do, with no sign-in and no shop switch
        // to notice — so the catalog has to be able to tell us itself.
        RolePermissionStore.Instance.RolesChanged += (_, _) => RefreshCapabilities();
    }

    /// <summary>Raised after the signed-in user or the shop their capabilities resolve against changed.</summary>
    public event EventHandler? CapabilitiesChanged;

    /// <summary>The account that signed in this session, or null before a successful sign-in.</summary>
    public UserAccount? CurrentUser { get; private set; }

    /// <summary>Full access to everything, in every shop.</summary>
    public bool IsAdministrator => CurrentUser?.IsAdministrator ?? false;

    /// <summary>
    /// The roles the signed-in user holds in the shop currently open, in catalog order. Empty when
    /// no shop is open or they hold none there; an administrator reports the administrator role
    /// everywhere.
    /// </summary>
    public IReadOnlyList<RoleDefinition> CurrentRoles()
        => _activeShopPublicId is { } shopId
            ? RolesFor(shopId)
            : AdministratorRoles();

    /// <summary>
    /// Whether the signed-in user may do one specific thing, right now.
    /// </summary>
    /// <remarks>
    /// THE ONE QUESTION THE APPLICATION ASKS. Every gate in the UI routes here, and the named
    /// properties below are only readable spellings of it — so a rule change is a change to the role
    /// catalog (data), not to the build.
    /// </remarks>
    public bool Can(AppCapability capability) => _capabilities.Contains(capability);

    /// <summary>
    /// Named capabilities rather than <see cref="Can"/> calls spread through the UI. They read
    /// better at the call site and they document, in one list, what the application gates at all.
    /// </summary>
    public bool CanViewOrders => Can(AppCapability.ViewOrders);

    /// <summary>Start a new order.</summary>
    public bool CanCreateOrders => Can(AppCapability.CreateOrders);

    /// <summary>Change a saved order. Without it the editor opens read-only, as a finished order does.</summary>
    public bool CanEditOrders => Can(AppCapability.EditOrders);

    /// <summary>Delete an order, singly or as a batch.</summary>
    public bool CanDeleteOrders => Can(AppCapability.DeleteOrders);

    /// <summary>Duplicate an order.</summary>
    public bool CanCopyOrders => Can(AppCapability.CopyOrders);

    /// <summary>Open the recycle bin: restore a deleted order, or destroy one for good.</summary>
    public bool CanManageRecycleBin => Can(AppCapability.ManageRecycleBin);

    /// <summary>Export the order list as a spreadsheet.</summary>
    public bool CanExportOrders => Can(AppCapability.ExportOrders);

    /// <summary>Change the backup schedule and restore the installation from a safety copy.</summary>
    public bool CanManageBackups => Can(AppCapability.ManageBackups);

    /// <summary>Print or download a receipt and a measurement sheet.</summary>
    public bool CanPrintOrderDocuments => Can(AppCapability.PrintOrderDocuments);

    /// <summary>Open the settlement report, and see the month's figures on the main window.</summary>
    public bool CanViewReports => Can(AppCapability.ViewReports);

    /// <summary>Print the settlement report or save it as a PDF.</summary>
    public bool CanExportReports => Can(AppCapability.ExportReports);

    /// <summary>Create a shop, and reach the store-management tools.</summary>
    public bool CanCreateShops => Can(AppCapability.CreateShops);

    /// <summary>Managing every account in the installation and its shop memberships.</summary>
    public bool CanManageUsers => Can(AppCapability.ManageUsers);

    /// <summary>Defining roles and what they may do.</summary>
    public bool CanManagePermissions => Can(AppCapability.ManagePermissions);

    /// <summary>
    /// The Local Database and Import/Export menus, and the database path in the status bar. These
    /// replace the whole installation's data, which is why the capability is installation-scoped.
    /// </summary>
    public bool CanUseDataTools => Can(AppCapability.UseDataTools);

    /// <summary>Whether the user may change the OPEN shop's own settings — details, currency, tax.</summary>
    public bool CanConfigureShop => Can(AppCapability.ConfigureShop);

    /// <summary>Whether the user may edit the open shop's measurement terms.</summary>
    public bool CanManageMeasurementTerms => Can(AppCapability.ManageMeasurementTerms);

    /// <summary>Whether the user may edit the open shop's product categories.</summary>
    public bool CanManageProductCatalog => Can(AppCapability.ManageProductCatalog);

    /// <summary>Whether the user may edit the receipt letterhead.</summary>
    public bool CanManageBranding => Can(AppCapability.ManageBranding);

    /// <summary>
    /// Whether the user may manage the OPEN shop's roster: who works there, their role, their shift
    /// and whether they are still active.
    /// </summary>
    public bool CanManageStoreMembers => Can(AppCapability.ManageStoreMembers);

    /// <summary>
    /// Whether the user may delete an account outright, as opposed to deactivating a membership.
    /// Deletion is installation-wide — the person may work in branches this user has never seen.
    /// </summary>
    public bool CanDeleteAccounts => Can(AppCapability.DeleteAccounts);

    /// <summary>
    /// Whether the user may run the application in ANY shipped language — everyone else is confined
    /// to the languages their shop installs, which may well be several, so this being false does NOT
    /// mean "no language toggle". <c>ShopLanguages</c> owns that question.
    /// </summary>
    /// <remarks>
    /// The login screen stays switchable for everyone regardless — no shop is open there, and a user
    /// has to be able to read the screen they sign in on.
    /// </remarks>
    public bool CanChooseAnyLanguage => Can(AppCapability.ChooseAnyLanguage);

    /// <summary>
    /// Points the capability set at a shop. Called from <c>App.ApplyActiveShop</c> BEFORE the shop is
    /// published to <see cref="ShopContext"/>, so anything reacting to that change already sees the
    /// new answers.
    /// </summary>
    public void BindShop(Shop? shop)
    {
        _activeShopPublicId = shop?.PublicId;
        RefreshCapabilities();
    }

    /// <summary>
    /// Re-resolves what the signed-in user may do, then announces it. One call, because a refresh
    /// without the notification leaves stale chrome and a notification without the refresh reports
    /// the previous answers.
    /// </summary>
    private void RefreshCapabilities()
    {
        _capabilities = ResolveCapabilities();
        CapabilitiesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Everything the signed-in user may do: the union of what their roles grant, resolved against
    /// the right memberships for each capability's scope.
    /// </summary>
    /// <remarks>
    /// Installation-scoped capabilities are answered by ANY active membership, because the screens
    /// that ask them run with no shop open — the shop picker asks "may this person create a shop"
    /// before there is a shop to resolve against, and a question that could only be answered "no"
    /// there is a capability that could never be granted.
    ///
    /// The administrator-only three are stripped at the end regardless of what any role claims, so a
    /// hand-edited <c>roles.json</c> cannot hand out the keys to the installation.
    /// </remarks>
    private HashSet<AppCapability> ResolveCapabilities()
    {
        if (CurrentUser is null)
            return new HashSet<AppCapability>();

        if (CurrentUser.IsAdministrator)
            return new HashSet<AppCapability>(Enum.GetValues<AppCapability>());

        var catalog = RolePermissionStore.Instance;
        var granted = new HashSet<AppCapability>();

        // Each membership's roles are resolved AGAINST THAT MEMBERSHIP'S OWN SHOP (v9.0): a role is a
        // name the installation shares, but what it grants can be varied per branch, so "Manager at
        // the Kensington workroom" is not necessarily the same set as "Manager downtown".
        foreach (var membership in CurrentUser.Memberships.Where(membership => membership.IsActive))
        {
            granted.UnionWith(catalog.CapabilitiesFor(membership.RoleIds, membership.ShopPublicId)
                .Where(capability =>
                    CapabilityCatalog.Entry(capability).Scope == CapabilityScope.Installation));
        }

        if (_activeShopPublicId is { } shopId
            && CurrentUser.Memberships.FirstOrDefault(membership =>
                membership.ShopPublicId == shopId && membership.IsActive) is { } inShop)
        {
            granted.UnionWith(catalog.CapabilitiesFor(inShop.RoleIds, shopId)
                .Where(capability => CapabilityCatalog.Entry(capability).Scope == CapabilityScope.Shop));
        }

        granted.RemoveWhere(capability => CapabilityCatalog.Entry(capability).AdministratorOnly);
        return granted;
    }

    /// <summary>
    /// Verifies a credential and, on success, records it as the signed-in user.
    /// </summary>
    /// <remarks>
    /// An unknown user name and a wrong password report the SAME failure, or the dialog becomes a
    /// user-name oracle. A deactivated account is reported distinctly: the credential was right and
    /// the person needs to be told to talk to their manager rather than to keep retyping.
    ///
    /// A record demanding a password change is refused too, and refused BEFORE the session exists:
    /// the alternative — hand out the session and ask nicely — is a prompt that can be closed, and
    /// the whole point is that the shipped password buys nothing. <see cref="ChangeOwnPassword"/>
    /// is the way through, and it needs no session because it takes the current password itself.
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

        // Before the password check, deliberately: somebody every shop has delisted should be told
        // that, not walked through choosing a password for an account they cannot use.
        if (IsLockedOut(record))
            return SignInResult.Failed(SignInFailure.Deactivated);

        if (record.MustChangePassword)
            return SignInResult.Failed(SignInFailure.PasswordChangeRequired);

        CurrentUser = ToAccount(record);
        RefreshCapabilities();
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
    /// Takes over another account's session without its password — "sign in as this user", offered
    /// to an administrator from the account screen.
    /// </summary>
    /// <remarks>
    /// This grants nothing an administrator did not already have: they can set any account's
    /// password, so they could reach the same session in two more clicks. What it buys is SEEING the
    /// application as somebody else — which shops they get, which chrome is hidden, what their
    /// language toggle offers — which is otherwise guesswork.
    ///
    /// Gated here as well as in the UI, unlike the roster edits nearby. Those only write data; this
    /// one hands out a session, so the check belongs where it cannot be skipped by a new call site.
    ///
    /// A locked-out account is refused. Signing in as somebody every shop has delisted would land
    /// the administrator on "no shop is available" and then back at the sign-in screen, having lost
    /// their own session to learn something the roster already shows.
    ///
    /// The bound shop is cleared: capabilities must not go on resolving against the shop the
    /// ADMINISTRATOR had open, which the new user may hold no role in at all. The caller opens a
    /// shop next, which binds one again.
    /// </remarks>
    public AccountOperationResult SignInAs(string userName)
    {
        if (!IsAdministrator)
            return AccountOperationResult.Protected;

        var record = FindRecord(userName);

        if (record is null)
            return AccountOperationResult.NotFound;

        if (IsCurrentUser(record.UserName))
            return AccountOperationResult.Protected;

        if (IsLockedOut(record))
            return AccountOperationResult.Deactivated;

        CurrentUser = ToAccount(record);
        _activeShopPublicId = null;
        RefreshCapabilities();
        return AccountOperationResult.Success;
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
        RefreshCapabilities();
    }

    /// <summary>
    /// The roles the signed-in user holds in a given shop, in catalog order. Empty when they hold
    /// none there, when their membership has been deactivated, or when every role they were given
    /// has since been deleted from the catalog.
    /// </summary>
    public IReadOnlyList<RoleDefinition> RolesFor(Guid shopPublicId)
    {
        if (CurrentUser is null)
            return Array.Empty<RoleDefinition>();

        if (CurrentUser.IsAdministrator)
            return AdministratorRoles();

        var membership = CurrentUser.Memberships
            .FirstOrDefault(candidate => candidate.ShopPublicId == shopPublicId);

        if (membership is not { IsActive: true })
            return Array.Empty<RoleDefinition>();

        var catalog = RolePermissionStore.Instance;

        return catalog.All()
            .Where(role => membership.RoleIds.Contains(role.Id, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Whether the signed-in user may open a given shop at all: an active membership naming at least
    /// one role that still exists.
    /// </summary>
    /// <remarks>
    /// "Still exists" is the load-bearing half. A membership whose every role has been deleted grants
    /// nothing, so letting it open the shop would hand somebody a window with no records, no buttons
    /// and no explanation — the picker refusing the shop is the truthful version of the same fact.
    /// </remarks>
    public bool CanAccessShop(Guid shopPublicId) => RolesFor(shopPublicId).Count > 0;

    /// <summary>Filters a shop list down to the ones the signed-in user may open.</summary>
    public List<Shop> FilterAccessibleShops(IEnumerable<Shop> shops)
    {
        ArgumentNullException.ThrowIfNull(shops);
        return shops.Where(shop => CanAccessShop(shop.PublicId)).ToList();
    }

























    // --- Internals ------------------------------------------------------------------------------

    private IReadOnlyList<RoleDefinition> AdministratorRoles()
        => IsAdministrator
            ? new[] { BuiltInRoles.Administrator() }
            : Array.Empty<RoleDefinition>();

    private bool IsCurrentUser(string userName)
        => CurrentUser is not null
            && string.Equals(CurrentUser.UserName, userName, StringComparison.OrdinalIgnoreCase);

    private void RefreshCurrentUser(CredentialRecord record)
    {
        if (IsCurrentUser(record.UserName))
            AdoptAsCurrentUser(record);
    }

    /// <summary>
    /// Re-snapshots the session from a record already known to be the signed-in one.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="RefreshCurrentUser"/> because that one identifies the session BY
    /// USER NAME, which a rename has just changed — the record would no longer match itself and the
    /// session would keep a login that no longer exists. A renaming caller therefore decides before
    /// the rename and calls this afterwards.
    /// </remarks>
    private void AdoptAsCurrentUser(CredentialRecord record)
    {
        CurrentUser = ToAccount(record);
        RefreshCapabilities();
    }

    private CredentialRecord? FindRecord(string userName)
        => _file.Users.Find(user =>
            string.Equals(user.UserName, userName, StringComparison.OrdinalIgnoreCase));

    // Copied rather than handed out: the session snapshot and the screens must not be able to edit
    // the file's in-memory state behind Save's back.
    private static UserAccount ToAccount(CredentialRecord record)
        => new(record.UserName, record.FirstName, record.LastName, record.PhoneNumber, record.Email,
            record.IsAdministrator, record.Memberships.Select(Clone).ToList());

    private static ShopMembership Clone(ShopMembership membership) => new()
    {
        ShopPublicId = membership.ShopPublicId,
        RoleIds = new List<string>(membership.RoleIds),
        IsActive = membership.IsActive,
        JoinedOn = membership.JoinedOn,
        DeactivatedOn = membership.DeactivatedOn,
        ShiftStart = membership.ShiftStart,
        ShiftEnd = membership.ShiftEnd
    };















}












