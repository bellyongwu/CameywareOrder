using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CameywareOrder.Models;
using Path = System.IO.Path;
using CameywareOrder.Configuration;
namespace CameywareOrder.Services;

// Passwords — one responsibility of AuthenticationService, split out in v9.3.0.
// A PARTIAL rather than a separate type: these members read the same private state as the rest of
// the service, and threading it through a new class's constructor would be shape for its own sake.
public sealed partial class AuthenticationService
{
    /// <summary>
    /// Accounts created ONCE, on the first load that does not already record them as provisioned.
    ///
    /// "Once" is the whole point: topping these up on every load — which is what this did before
    /// accounts could be managed in the UI — would resurrect an account the administrator had
    /// deliberately deleted. <see cref="CredentialFile.ProvisionedAccounts"/> is the record that
    /// makes a deletion stick. <see cref="AdministratorUserName"/> is deliberately exempt.
    /// </summary>
    /// <remarks>
    /// ONE account, and that is a deliberate reduction. Until v9.2.0 this also seeded
    /// <c>manager</c>, <c>staff</c>, <c>test1</c> and <c>test2</c>, each with its user name as its
    /// password — four standing credentials on every installation, present so a developer had
    /// accounts to click through the assignment screen with. That is a development convenience
    /// billed to every shop that installs the product, and the roster screen creates real staff
    /// accounts anyway. They are gone; <see cref="HistoricalSeedPasswords"/> is what deals with the
    /// installations that already have them.
    /// </remarks>
    private static readonly (string UserName, string Password, bool IsAdministrator)[] SeedAccounts =
    {
        (AdministratorUserName, AdministratorInitialPassword, true)
    };

    /// <summary>
    /// Every user name this product has ever seeded, with the password it seeded it with.
    /// </summary>
    /// <remarks>
    /// Read once, by the schema-6 upgrade, to find accounts on an EXISTING installation that are
    /// still signed into with the password that shipped. Those are marked must-change; nothing is
    /// deleted. An account named <c>staff</c> may well be a real person by now — the shop's data is
    /// not ours to remove because we regret having created it — but a shipped password is a
    /// published password, and the one thing we can do about it is refuse to let it open a session
    /// again.
    ///
    /// This list must never shrink. An entry removed from it is an installation that keeps a known
    /// credential forever, and nothing anywhere would report it.
    /// </remarks>
    private static readonly (string UserName, string Password)[] HistoricalSeedPasswords =
    {
        (AdministratorUserName, AdministratorInitialPassword),
        ("manager", "manager"),
        ("staff", "staff"),
        ("test1", "test1"),
        ("test2", "test2")
    };

    /// <summary>
    /// Replaces one's OWN password, proving the current one rather than relying on a session. On
    /// success the account no longer owes a change, so the caller can sign in with the new password
    /// immediately.
    /// </summary>
    /// <remarks>
    /// Deliberately usable with nobody signed in. The path that most needs it is the sign-in screen
    /// refusing a shipped password, where by construction there is no session yet — a method that
    /// required one would have to be reached by first granting the thing it exists to withhold.
    ///
    /// It is not an authorization hole: knowing the current password is exactly the credential
    /// <see cref="Authenticate"/> asks for. What it cannot do is anything else — no role, no
    /// membership, no other account.
    /// </remarks>
    public AccountOperationResult ChangeOwnPassword(
        string userName, string currentPassword, string newPassword)
    {
        var record = FindRecord(userName);

        if (record is null || !Verify(currentPassword, record))
            return AccountOperationResult.NotFound;

        if (IsLockedOut(record))
            return AccountOperationResult.Deactivated;

        return WritePassword(record, newPassword, requireChange: false);
    }

    /// <summary>
    /// Replaces an account's password on somebody else's behalf. Allowed for every account,
    /// including the administrator.
    /// </summary>
    /// <param name="requireChange">
    /// Whether the account must replace this password before it can sign in again. REQUIRED rather
    /// than defaulted, and that is the point: a default would let a new call site inherit whichever
    /// answer happened to be written here, and the two callers want opposite things. An
    /// administrator handing over a password wants <c>true</c> — they have just read it aloud, and
    /// the person it belongs to should be the only one who knows the next one. A harness pinning a
    /// fixture password wants <c>false</c>, because it then signs in with it.
    /// </param>
    public AccountOperationResult SetPassword(string userName, string password, bool requireChange)
    {
        var record = FindRecord(userName);

        if (record is null)
            return AccountOperationResult.NotFound;

        return WritePassword(record, password, requireChange);
    }

    /// <summary>
    /// The ONE place a password is written. Both entry points come through here, so the policy
    /// cannot hold on one path and not the other.
    /// </summary>
    private AccountOperationResult WritePassword(
        CredentialRecord record, string password, bool requireChange)
    {
        var rejection = CheckPassword(record.UserName, password);

        if (rejection != AccountOperationResult.Success)
            return rejection;

        var salt = RandomNumberGenerator.GetBytes(SaltByteCount);
        record.Iterations = DefaultIterations;
        record.Salt = Convert.ToBase64String(salt);
        record.Hash = Convert.ToBase64String(DeriveHash(password, salt, DefaultIterations));
        record.MustChangePassword = requireChange;

        Save(_file);
        RefreshCurrentUser(record);
        return AccountOperationResult.Success;
    }

    /// <summary>
    /// Whether a password may be stored for an account, and why not when it may not.
    /// </summary>
    /// <remarks>
    /// Two rules, and the second is the one that matters. A minimum length is ordinary hygiene; the
    /// bar on a password equal to its user name is what makes a forced change mean something,
    /// because otherwise the answer to "replace <c>admin</c>/<c>admin</c>" is to type <c>admin</c>
    /// again and the whole mechanism has moved the problem by one dialog.
    ///
    /// Case-insensitive, matching how a user name is resolved everywhere else: <c>Admin</c> is the
    /// same login as <c>admin</c>, so it is the same bad password.
    /// </remarks>
    private static AccountOperationResult CheckPassword(string userName, string password)
    {
        if (string.IsNullOrEmpty(password))
            return AccountOperationResult.PasswordRequired;

        if (password.Length < MinimumPasswordLength)
            return AccountOperationResult.PasswordTooShort;

        if (string.Equals(password.Trim(), (userName ?? string.Empty).Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            return AccountOperationResult.PasswordSameAsUserName;
        }

        return AccountOperationResult.Success;
    }

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
}
