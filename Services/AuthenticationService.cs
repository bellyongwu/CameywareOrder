using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LeeYongeOrdering.Models;
using Path = System.IO.Path;

namespace LeeYongeOrdering.Services;

/// <summary>
/// Sign-in for the application. Accounts live in <c>credentials.json</c> under the app's local
/// AppData folder, seeded with a single <c>admin</c> / <c>admin</c> administrator on first run.
///
/// Deliberately file-backed rather than a database table, for two reasons: a corrupt or locked
/// database still lets you reach the login screen, and — more importantly — accounts are NOT wiped
/// by 本地配置 → 导入 → 数据库, which replaces the whole database file wholesale.
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
    /// Accounts guaranteed to exist. Manager and Staff are seeded so the role-dependent behaviour
    /// can be exercised before an account-management screen exists; their password matches their
    /// user name, same as the administrator.
    ///
    /// Missing entries are topped up on every load, so an installation created before a role
    /// existed gains it without a file migration. Once accounts can be managed in the UI this must
    /// become first-run-only, or a deliberately deleted account would keep reappearing.
    /// </summary>
    private static readonly (string UserName, string Password, UserRole Role)[] DefaultAccounts =
    {
        ("admin", "admin", UserRole.Admin),
        ("manager", "manager", UserRole.Manager),
        ("staff", "staff", UserRole.Staff)
    };

    // PBKDF2-HMAC-SHA256. Stored per record so the cost can be raised later without invalidating
    // existing accounts.
    private const int DefaultIterations = 100_000;
    private const int SaltByteCount = 16;
    private const int HashByteCount = 32;

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    // DECLARED LAST ON PURPOSE. Static field initializers run in textual order, and constructing
    // the singleton reads DefaultAccounts and SerializerOptions — so declaring it above them left
    // both null during construction and the type initializer threw
    // TypeInitializationException wrapping a NullReferenceException, which surfaced as every
    // sign-in failing regardless of the credentials entered. Keep this below everything it uses.
    public static AuthenticationService Instance { get; } = new();

    private readonly CredentialFile _file;

    private AuthenticationService()
    {
        _file = LoadOrSeed();
    }

    /// <summary>The account that signed in this session, or null before a successful sign-in.</summary>
    public UserAccount? CurrentUser { get; private set; }

    /// <summary>
    /// Named capabilities rather than role comparisons spread through the UI — when the rules grow,
    /// only these change.
    /// </summary>
    public bool CanManageShops => CurrentUser?.Role == UserRole.Admin;

    /// <summary>
    /// Whether the user may run the application in a language of their choosing. Only an
    /// administrator can; everyone else follows the language the shop is configured for, so a
    /// branch's staff all see the same thing. (The login screen itself stays switchable for
    /// everyone — otherwise a user could not read the screen they sign in on.)
    /// </summary>
    public bool CanChooseLanguage => CurrentUser?.Role == UserRole.Admin;

    /// <summary>
    /// Verifies a credential and, on success, records it as the signed-in user. Returns null when
    /// the user name is unknown or the password does not match — the caller must not be told which,
    /// or the dialog becomes a user-name oracle.
    /// </summary>
    public UserAccount? Authenticate(string userName, string password)
    {
        var record = _file.Users.FirstOrDefault(user =>
            string.Equals(user.UserName, userName, StringComparison.OrdinalIgnoreCase));

        if (record is null)
        {
            // Hash anyway so an unknown user name costs the same time as a wrong password, and the
            // response time cannot be used to enumerate accounts.
            _ = DeriveHash(password, RandomNumberGenerator.GetBytes(SaltByteCount), DefaultIterations);
            return null;
        }

        if (!Verify(password, record))
            return null;

        CurrentUser = new UserAccount(record.UserName, record.Role);
        return CurrentUser;
    }

    /// <summary>
    /// Ends the session. Every capability gate reads <see cref="CurrentUser"/>, so clearing it
    /// immediately revokes them — which is why the caller must take down the main window before
    /// calling this, not after.
    /// </summary>
    public void SignOut() => CurrentUser = null;

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

    private static CredentialRecord CreateRecord(string userName, string password, UserRole role)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltByteCount);
        return new CredentialRecord
        {
            UserName = userName,
            Role = role,
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
            "LeeYongeOrdering");

    private static string SettingFilePath => Path.Combine(SettingDirectory, FileName);

    private static CredentialFile LoadOrSeed()
    {
        // A missing or corrupt file starts empty rather than throwing: deleting it is the only
        // password-reset path, and it must not lock the shop out of its own application.
        var file = TryLoad() ?? new CredentialFile();

        var added = false;
        foreach (var (userName, password, role) in DefaultAccounts)
        {
            if (file.Users.Any(user => string.Equals(user.UserName, userName, StringComparison.OrdinalIgnoreCase)))
                continue;

            file.Users.Add(CreateRecord(userName, password, role));
            added = true;
        }

        if (added)
            Save(file);

        return file;
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

/// <summary>The signed-in user, as the rest of the app sees them.</summary>
public sealed record UserAccount(string UserName, UserRole Role);

/// <summary>
/// Shape of <c>credentials.json</c>. A LIST from the outset even though only one account is seeded,
/// so adding Manager and Staff later needs no file-format change or migration.
/// </summary>
public sealed class CredentialFile
{
    public List<CredentialRecord> Users { get; set; } = new();
}

public sealed class CredentialRecord
{
    public string UserName { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Admin;
    public int Iterations { get; set; }
    public string Salt { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
    /// <summary>Reserved: seeded true for the default admin, not yet enforced anywhere.</summary>
    public bool MustChangePassword { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
