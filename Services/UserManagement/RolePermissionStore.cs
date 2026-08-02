using System.IO;
using System.Text;
using System.Text.Json;
using CameywareOrder.Configuration;
using CameywareOrder.Models;
using Path = System.IO.Path;

namespace CameywareOrder.Services;

/// <summary>
/// The installation's role catalog: which roles exist and what each of them may do.
///
/// Stored in <c>roles.json</c> beside <c>credentials.json</c>, and for the same reason — a role is
/// part of who may use the installation, not part of a shop's trading data, so replacing the whole
/// database through Import must not silently rewrite everybody's permissions.
/// </summary>
/// <remarks>
/// THE ADMINISTRATOR IS NEVER READ FROM THE FILE. It is regenerated from
/// <see cref="BuiltInRoles.Administrator"/> on every load, because it is defined as "every
/// capability there is" — persisting it would freeze the list as it stood the day the file was
/// written, so the next release's new capability would be missing from the one role that is supposed
/// to have all of them, and nobody would notice until somebody could not click something.
///
/// The other built-ins ARE persisted: they are editable, so their stored set is the answer.
/// Whatever is missing is topped up on load, which is what makes a deleted or corrupt file recover
/// to a usable installation rather than to one where no manager can configure anything.
///
/// SCOPE, as with <c>AuthenticationService</c>: this is an access gate, not a security boundary. A
/// local user can edit the file.
/// </remarks>
public sealed class RolePermissionStore
{
    private const string FileName = "roles.json";

    private const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public static RolePermissionStore Instance { get; } = new();

    private RoleFile _file;

    private RolePermissionStore()
    {
        _file = LoadOrSeed();
    }

    /// <summary>Raised after any role is created, renamed, re-scoped or removed.</summary>
    public event EventHandler? RolesChanged;

    /// <summary>
    /// Every role, administrator first and then in the order they were defined.
    /// </summary>
    public IReadOnlyList<RoleDefinition> All()
    {
        var roles = new List<RoleDefinition> { BuiltInRoles.Administrator() };
        roles.AddRange(_file.Roles.Select(ToDefinition));
        return roles;
    }

    /// <summary>The roles that can actually be given to somebody in a shop.</summary>
    public IReadOnlyList<RoleDefinition> Assignable()
        => All().Where(role => role.IsAssignable).ToList();

    /// <summary>One role by its id, or null when nothing carries that id any more.</summary>
    public RoleDefinition? Find(string? roleId)
        => roleId is null ? null : All().FirstOrDefault(role => IdMatches(role.Id, roleId));

    /// <summary>
    /// Every capability granted by a set of role ids. An id that resolves to nothing contributes
    /// nothing — a membership naming a deleted role fails CLOSED rather than falling back to a
    /// default set somebody would have to guess at.
    /// </summary>
    public IReadOnlySet<AppCapability> CapabilitiesFor(IEnumerable<string> roleIds)
    {
        ArgumentNullException.ThrowIfNull(roleIds);

        var granted = new HashSet<AppCapability>();

        foreach (var role in roleIds.Select(Find).OfType<RoleDefinition>())
            granted.UnionWith(role.Capabilities);

        return granted;
    }

    /// <summary>Whether a name is already taken by a role other than <paramref name="ignoringId"/>.</summary>
    /// <remarks>
    /// Compared against the name as an administrator SEES it, which for a shipped role is its
    /// translation. Two roles both reading "Manager" on screen are indistinguishable in every list
    /// that offers them, whichever of them carries the string-table key.
    /// </remarks>
    public bool IsNameTaken(Localization.ILocalizedText text, string? name, string? ignoringId = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        var wanted = (name ?? string.Empty).Trim();

        if (wanted.Length == 0)
            return false;

        return All().Any(role =>
            !IdMatches(role.Id, ignoringId)
            && string.Equals(role.ResolveName(text), wanted, StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>
    /// Defines a new role. Its id is derived from the name once and then fixed, so renaming it later
    /// costs nobody their membership.
    /// </summary>
    public RoleOperationResult Create(
        Localization.ILocalizedText text, string name, IEnumerable<AppCapability> capabilities, out string? createdId)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(capabilities);

        createdId = null;

        var wanted = (name ?? string.Empty).Trim();

        if (wanted.Length == 0)
            return RoleOperationResult.NameRequired;

        if (IsNameTaken(text, wanted))
            return RoleOperationResult.NameTaken;

        var id = UniqueId(wanted);

        _file.Roles.Add(new RoleRecord
        {
            Id = id,
            CustomName = wanted,
            IsBuiltIn = false,
            Capabilities = Grantable(capabilities).Select(capability => capability.ToString()).ToList()
        });

        Save();
        createdId = id;
        return RoleOperationResult.Success;
    }

    /// <summary>Renames a role. The administrator cannot be renamed; a built-in can.</summary>
    public RoleOperationResult Rename(Localization.ILocalizedText text, string roleId, string name)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (IsAdministrator(roleId))
            return RoleOperationResult.Protected;

        var record = FindRecord(roleId);

        if (record is null)
            return RoleOperationResult.NotFound;

        var wanted = (name ?? string.Empty).Trim();

        if (wanted.Length == 0)
            return RoleOperationResult.NameRequired;

        if (IsNameTaken(text, wanted, roleId))
            return RoleOperationResult.NameTaken;

        record.CustomName = wanted;

        // A shipped role that has been given a name of its own stops following the string table:
        // the administrator has overruled it, and a translation switch must not undo that.
        record.NameKey = null;

        Save();
        return RoleOperationResult.Success;
    }

    /// <summary>Replaces what a role may do.</summary>
    public RoleOperationResult SetCapabilities(string roleId, IEnumerable<AppCapability> capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        if (IsAdministrator(roleId))
            return RoleOperationResult.Protected;

        var record = FindRecord(roleId);

        if (record is null)
            return RoleOperationResult.NotFound;

        record.Capabilities = Grantable(capabilities).Select(capability => capability.ToString()).ToList();
        Save();
        return RoleOperationResult.Success;
    }

    /// <summary>
    /// Removes a role, and with it every assignment of that role.
    /// </summary>
    /// <remarks>
    /// The two halves are ONE operation on purpose. A role deleted from the catalog while
    /// memberships still name it leaves people holding an id that resolves to nothing — they keep
    /// their place in the shop and lose every capability, which reads on screen as the application
    /// having broken rather than as a permission having been withdrawn. The caller is expected to
    /// have told the administrator how many people that is; <c>HoldersOf</c> answers it.
    /// </remarks>
    public RoleOperationResult Delete(string roleId)
    {
        if (IsAdministrator(roleId))
            return RoleOperationResult.Protected;

        var record = FindRecord(roleId);

        if (record is null)
            return RoleOperationResult.NotFound;

        if (record.IsBuiltIn)
            return RoleOperationResult.Protected;

        AuthenticationService.Instance.DropRole(record.Id);

        _file.Roles.Remove(record);
        Save();
        return RoleOperationResult.Success;
    }

    /// <summary>Restores a built-in role's shipped capability set.</summary>
    public RoleOperationResult RestoreDefaults(string roleId)
    {
        var shipped = BuiltInRoles.All()
            .FirstOrDefault(role => IdMatches(role.Id, roleId) && !role.IsAdministratorRole);

        if (shipped is null)
            return RoleOperationResult.NotFound;

        return SetCapabilities(roleId, shipped.Capabilities);
    }

    // --- Internals ----------------------------------------------------------------------------

    private static bool IsAdministrator(string? roleId) => IdMatches(RoleDefinition.AdministratorId, roleId);

    private static bool IdMatches(string? left, string? right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private RoleRecord? FindRecord(string? roleId)
        => roleId is null ? null : _file.Roles.Find(record => IdMatches(record.Id, roleId));

    /// <summary>Drops anything a role is not allowed to hold, wherever the list came from.</summary>
    private static IEnumerable<AppCapability> Grantable(IEnumerable<AppCapability> capabilities)
        => capabilities.Distinct().Where(CapabilityCatalog.IsGrantable).OrderBy(capability => capability);

    private static RoleDefinition ToDefinition(RoleRecord record) => new(
        record.Id,
        record.CustomName,
        record.NameKey,
        record.IsBuiltIn,
        record.Capabilities.Select(ParseCapability).OfType<AppCapability>());

    /// <summary>
    /// A capability name the build does not know is dropped rather than thrown on: the file may have
    /// been written by a newer release, and refusing to load it would lock the installation out of
    /// every permission it has.
    /// </summary>
    private static AppCapability? ParseCapability(string name)
        => Enum.TryParse<AppCapability>(name, ignoreCase: false, out var capability)
            && Enum.IsDefined(capability)
                ? capability
                : null;

    /// <summary>
    /// An id from a typed name: lower-case, words joined by hyphens, and anything that is not a
    /// letter or a digit dropped. A name with nothing usable in it — one written entirely in a script
    /// this strips — still gets an id, because the id is machine-facing and the NAME is what anybody
    /// reads.
    /// </summary>
    private string UniqueId(string name)
    {
        var slug = new StringBuilder();

        foreach (var character in name.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
                slug.Append(character);
            else if (slug.Length > 0 && slug[^1] != '-')
                slug.Append('-');
        }

        var stem = slug.ToString().Trim('-');

        if (stem.Length == 0)
            stem = "role";

        var candidate = stem;
        var suffix = 2;

        while (Find(candidate) is not null)
        {
            candidate = $"{stem}-{suffix}";
            suffix++;
        }

        return candidate;
    }

    private static RoleFile LoadOrSeed()
    {
        var existing = TryLoad();
        var file = existing ?? new RoleFile { SchemaVersion = CurrentSchemaVersion };

        var changed = existing is null;
        changed |= TopUpBuiltIns(file);
        changed |= ProvisionExampleRoles(file);

        // Never stored: regenerated on every load so it always holds every capability that exists.
        changed |= file.Roles.RemoveAll(record => IsAdministrator(record.Id)) > 0;

        if (changed)
            Write(file);

        return file;
    }

    /// <summary>
    /// Puts back any shipped role the file is missing. This is what makes a lost or hand-edited file
    /// recover: without it, deleting <c>roles.json</c> would leave an installation where every
    /// manager's membership names a role that no longer exists.
    /// </summary>
    private static bool TopUpBuiltIns(RoleFile file)
    {
        var added = false;

        foreach (var shipped in BuiltInRoles.All().Where(role => !role.IsAdministratorRole))
        {
            if (file.Roles.Exists(record => IdMatches(record.Id, shipped.Id)))
                continue;

            file.Roles.Add(ToRecord(shipped));
            added = true;
        }

        return added;
    }

    /// <summary>
    /// Creates the example roles ONCE. Auditor is shipped as an ordinary role rather than a built-in
    /// so that an installation which does not want it can delete it and have the deletion stick —
    /// the same bargain <c>AuthenticationService</c>'s seed accounts make.
    /// </summary>
    private static bool ProvisionExampleRoles(RoleFile file)
    {
        var auditor = BuiltInRoles.Auditor();

        if (file.ProvisionedRoles.Contains(auditor.Id, StringComparer.OrdinalIgnoreCase))
            return false;

        if (!file.Roles.Exists(record => IdMatches(record.Id, auditor.Id)))
            file.Roles.Add(ToRecord(auditor));

        file.ProvisionedRoles.Add(auditor.Id);
        return true;
    }

    private static RoleRecord ToRecord(RoleDefinition role) => new()
    {
        Id = role.Id,
        CustomName = role.CustomName,
        NameKey = role.NameKey,
        IsBuiltIn = role.IsBuiltIn,
        Capabilities = role.Capabilities
            .OrderBy(capability => capability)
            .Select(capability => capability.ToString())
            .ToList()
    };

    private static string SettingFilePath => UserDataPaths.ResolveConfigFile(FileName);

    private static RoleFile? TryLoad()
    {
        try
        {
            if (!File.Exists(SettingFilePath))
                return null;

            return JsonSerializer.Deserialize<RoleFile>(File.ReadAllText(SettingFilePath));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Unreadable is treated as absent, and the built-ins are then topped up: a corrupt file
            // must not be able to leave the installation with no roles at all.
            return null;
        }
    }

    private void Save()
    {
        Write(_file);
        RolesChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void Write(RoleFile file)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingFilePath)!);
            File.WriteAllText(SettingFilePath, JsonSerializer.Serialize(file, SerializerOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Non-fatal, matching the other stores: this session still holds the change in memory.
        }
    }

    /// <summary>Re-reads the file. For harnesses, which write it behind the singleton's back.</summary>
    internal void Reload()
    {
        _file = LoadOrSeed();
        RolesChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>Outcome of a role edit, so the caller can render its own localized message.</summary>
public enum RoleOperationResult
{
    Success,
    NameRequired,
    NameTaken,
    NotFound,

    /// <summary>A shipped role being deleted, or the administrator being changed at all.</summary>
    Protected
}

/// <summary>Shape of <c>roles.json</c>.</summary>
public sealed class RoleFile
{
    public int SchemaVersion { get; set; }

    /// <summary>
    /// Every example role this installation has already created once. It is what makes deleting a
    /// seeded role permanent — without it the next launch would create it again.
    /// </summary>
    public List<string> ProvisionedRoles { get; set; } = new();

    public List<RoleRecord> Roles { get; set; } = new();
}

/// <summary>One role as the file stores it. Capabilities are NAMES — see <see cref="AppCapability"/>.</summary>
public sealed class RoleRecord
{
    public string Id { get; set; } = string.Empty;

    public string? CustomName { get; set; }

    public string? NameKey { get; set; }

    public bool IsBuiltIn { get; set; }

    public List<string> Capabilities { get; set; } = new();
}
