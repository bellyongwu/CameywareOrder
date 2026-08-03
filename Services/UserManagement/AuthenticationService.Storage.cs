using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CameywareOrder.Models;
using Path = System.IO.Path;
using CameywareOrder.Configuration;
namespace CameywareOrder.Services;

// Storage — one responsibility of AuthenticationService, split out in v9.3.0.
// A PARTIAL rather than a separate type: these members read the same private state as the rest of
// the service, and threading it through a new class's constructor would be shape for its own sake.
public sealed partial class AuthenticationService
{
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

        foreach (var record in file.Users)
            SplitLegacyName(record);

        foreach (var membership in file.Users.SelectMany(record => record.Memberships))
            MigrateMembershipRoles(membership);

        ArmShippedPasswords(file);

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

    /// <summary>
    /// Schema 6: marks every account still signed into with the password this product shipped it
    /// with as owing a change. Existing installations only — a fresh file has nothing to find.
    /// </summary>
    /// <remarks>
    /// Verifying rather than trusting the name. An installation that has been running for a year
    /// may well have given <c>staff</c> a real password months ago, and demanding a change from
    /// somebody who already did the right thing is a support call about a security fix. So each
    /// candidate is checked against the password it was created with, and only a match is armed.
    ///
    /// This is the expensive part of the upgrade — one PBKDF2 derivation per matching name, at
    /// 100,000 iterations. It runs on the ONE load that upgrades the file and never again, which is
    /// why it lives in <see cref="UpgradeAccountShape"/> behind the version check rather than in
    /// <see cref="LoadOrSeed"/> where it would cost that on every launch forever.
    /// </remarks>
    private static void ArmShippedPasswords(CredentialFile file)
    {
        foreach (var (userName, password) in HistoricalSeedPasswords)
        {
            var record = file.Users.Find(candidate =>
                string.Equals(candidate.UserName, userName, StringComparison.OrdinalIgnoreCase));

            if (record is not null && !record.MustChangePassword && Verify(password, record))
                record.MustChangePassword = true;
        }
    }

    /// <summary>
    /// Splits a schema-3 single name into first and last.
    /// </summary>
    /// <remarks>
    /// The rule is deliberately conservative, because a wrong guess here renames a real person in a
    /// way nobody would think to check:
    ///
    ///  * NO whitespace — a Chinese name, "Prince" — the whole thing becomes the FIRST name and the
    ///    last is left empty. A Chinese name carries the family name first and has no separator, so a
    ///    positional guess would greet somebody by their surname alone. Keeping it
    ///    whole is right for that case and merely incomplete for a mononym, which is the better of
    ///    the two failure modes.
    ///  * Whitespace present — split at the LAST space. "Mary Jane Watson" gives "Mary Jane" +
    ///    "Watson", which is right far more often than splitting at the first space would be.
    ///
    /// Either way the value is preserved: nothing is dropped, and re-joining the two halves gives
    /// the original back.
    /// </remarks>
    private static void SplitLegacyName(CredentialRecord record)
    {
        var legacy = record.LegacyDisplayName?.Trim();
        record.LegacyDisplayName = null;

        // A record already carrying either half has been through this, or was written by a build
        // that knows about both — do not overwrite it from a stale single field.
        if (string.IsNullOrEmpty(legacy)
            || !string.IsNullOrWhiteSpace(record.FirstName)
            || !string.IsNullOrWhiteSpace(record.LastName))
        {
            return;
        }

        var lastSpace = legacy.LastIndexOf(' ');

        if (lastSpace <= 0)
        {
            record.FirstName = legacy;
            return;
        }

        record.FirstName = legacy[..lastSpace].Trim();
        record.LastName = legacy[(lastSpace + 1)..].Trim();
    }

    /// <summary>
    /// Turns a schema-4 membership's fixed <see cref="UserRole"/> list into role IDS.
    /// </summary>
    /// <remarks>
    /// Deliberately does NOT run through <see cref="NormalizeRoleIds"/>, which orders ids by the
    /// catalog and therefore drops any it does not recognise. That is right for a save and wrong
    /// here: this runs during the very first load, and dropping an unrecognised id would be a
    /// migration that discards the thing it was written to preserve.
    /// </remarks>
    private static void MigrateMembershipRoles(ShopMembership membership)
    {
        if (membership.LegacyRoles is not { Count: > 0 } legacy)
        {
            membership.LegacyRoles = null;
            return;
        }

        // A record already carrying ids has been through this, or was written by a build that knows
        // about both — do not overwrite it from the stale field.
        if (membership.RoleIds.Count == 0)
        {
            membership.RoleIds = legacy
                .Select(LegacyRoleIds.For)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        membership.LegacyRoles = null;
    }

    private static void FoldAssignmentsIntoMemberships(CredentialRecord record)
    {
        var grouped = record.LegacyAssignments!
            .GroupBy(assignment => assignment.ShopPublicId)
            .Select(group => new ShopMembership
            {
                ShopPublicId = group.Key,
                RoleIds = NormalizeRoleIds(group.Select(assignment => LegacyRoleIds.For(assignment.Role)))
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

        foreach (var seed in SeedAccounts)
        {
            if (!NeedsProvisioning(file, seed))
                continue;

            file.Users.Add(CreateRecord(seed.UserName, seed.Password, seed.IsAdministrator));

            if (!file.ProvisionedAccounts.Contains(seed.UserName, StringComparer.OrdinalIgnoreCase))
                file.ProvisionedAccounts.Add(seed.UserName);

            added = true;
        }

        return added;
    }

    /// <summary>Whether a seed account is missing and has not already been created once.</summary>
    /// <remarks>
    /// **The administrator is identified by its FLAG, never by its name.** "Is there an account
    /// called admin" is not the question that was meant — "is there an administrator" is, and it is
    /// the one that keeps the guarantee that matters: an installation can never end up with nobody
    /// able to administer it. It also means the invariant holds structurally rather than resting on
    /// the rename guard alone; asking by name, a login that somehow changed would leave the next
    /// load minting a SECOND administrator carrying a default password.
    ///
    /// Every other seed account is created ONCE. <see cref="CredentialFile.ProvisionedAccounts"/>
    /// records that it happened, which is what makes deleting a seeded account stick — and why a
    /// rename must leave that record alone (see <see cref="ApplyRename"/>).
    /// </remarks>
    private static bool NeedsProvisioning(
        CredentialFile file, (string UserName, string Password, bool IsAdministrator) seed)
    {
        if (seed.IsAdministrator)
            return !file.Users.Exists(user => user.IsAdministrator);

        if (file.Users.Exists(user =>
                string.Equals(user.UserName, seed.UserName, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return !file.ProvisionedAccounts.Contains(seed.UserName, StringComparer.OrdinalIgnoreCase);
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
