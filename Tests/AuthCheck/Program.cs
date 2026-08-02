using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using CameywareOrder.Configuration;
using CameywareOrder.Services;
using Path = System.IO.Path;

namespace AuthCheck;

/// <summary>
/// The v9.2.0 first-run hardening: what this product ships as a credential, and what it takes to
/// replace one.
/// </summary>
/// <remarks>
/// WHY SO MUCH OF THIS IS REFLECTION, stated so nobody "improves" it into something that writes on
/// the user's machine. <c>AuthenticationService</c> is a singleton over the real
/// <c>credentials.json</c>, and there is no seam to point it at a fixture — <c>UserDataPaths.Root</c>
/// resolves through <c>Environment.GetFolderPath</c>. So the rules themselves are exercised where
/// they live: <c>CheckPassword</c> and <c>ArmShippedPasswords</c> are pure static functions and are
/// called directly, which is both safer and a sharper test than driving a screen at them.
///
/// The refusal paths of <c>Authenticate</c> and <c>ChangeOwnPassword</c> ARE driven end to end, on a
/// throwaway instance whose in-memory file has a fixture account pushed into it. Every one of those
/// paths returns before the service saves, which is what makes it safe — and is asserted, not
/// assumed: the file is hashed either side and the run fails if it moved.
///
/// The success path of <c>ChangeOwnPassword</c> does save, so it is the one section that snapshots
/// the file and restores it in a finally. That section is deliberately last and deliberately small.
/// </remarks>
internal static class Program
{
    private static int _passed;
    private static int _failed;

    private const string GoodPassword = "fixture-Passw0rd";
    private const string SecondGoodPassword = "fixture-Passw0rd-2";

    private static void Check(string what, bool ok, string detail = "")
    {
        if (ok) { _passed++; Console.WriteLine($"  PASS  {what}"); }
        else { _failed++; Console.WriteLine($"  FAIL  {what}   {detail}"); }
    }

    private static int Main()
    {
        CameywareOrder.Tests.RepoPaths.UseRepositoryAsWorkingDirectory();

        var credentials = UserDataPaths.ResolveConfigFile("credentials.json");

        // Touched before the baseline, exactly as uicheck does: the singleton upgrades an
        // older-schema file on its first read, and that legitimate one-time write must not be
        // mistaken for this harness scribbling on the user's accounts.
        _ = AuthenticationService.Instance;
        var before = HashOf(credentials);

        Console.WriteLine("-- what ships");
        CheckSeedSet();

        Console.WriteLine("-- password policy");
        CheckPasswordPolicy();

        Console.WriteLine("-- upgrading an installation that already has the shipped passwords");
        CheckArmShippedPasswords();

        Console.WriteLine("-- the sign-in gate");
        CheckSignInRefusesAnUnchangedPassword();

        var afterReadOnlySections = HashOf(credentials);
        Check("credentials.json untouched by the read-only sections",
            before == afterReadOnlySections);

        Console.WriteLine("-- changing your own password (writes; restored afterwards)");
        CheckOwnPasswordChange(credentials);

        var after = HashOf(credentials);
        Check("credentials.json restored byte for byte", before == after,
            "the user's accounts were left modified — restore from the .bak beside them");

        Console.WriteLine();
        Console.WriteLine($"{_passed} passed, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }

    // --- What ships -------------------------------------------------------------------------

    private static void CheckSeedSet()
    {
        var seeds = Field<Array>("SeedAccounts");

        Check("exactly one account is seeded", seeds.Length == 1,
            $"found {seeds.Length} — a shipped account is a published credential");

        var only = (ITuple)seeds.GetValue(0)!;
        Check("the one seeded account is the administrator", (bool)only[2]!);

        // The four that used to ship. Named literally rather than read from the historical list,
        // so removing a name from that list cannot also remove the check that it is gone.
        foreach (var retired in new[] { "manager", "staff", "test1", "test2" })
        {
            var present = Enumerable.Range(0, seeds.Length)
                .Select(index => (string)((ITuple)seeds.GetValue(index)!)[0]!)
                .Contains(retired, StringComparer.OrdinalIgnoreCase);

            Check($"'{retired}' is no longer seeded", !present);
        }

        var historical = Field<Array>("HistoricalSeedPasswords");
        var historicalNames = Enumerable.Range(0, historical.Length)
            .Select(index => (string)((ITuple)historical.GetValue(index)!)[0]!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // This list must never shrink: an entry dropped from it is an existing installation that
        // keeps a known credential forever, with nothing anywhere to report it.
        foreach (var name in new[] { "admin", "manager", "staff", "test1", "test2" })
            Check($"'{name}' is still remembered as a shipped password", historicalNames.Contains(name));

        var record = CreateRecord("someone", GoodPassword, isAdministrator: false);
        Check("a newly created account owes a password change", record.MustChangePassword);

        var parameter = typeof(AuthenticationService)
            .GetMethod(nameof(AuthenticationService.SetPassword))!
            .GetParameters()
            .Single(candidate => candidate.Name == "requireChange");

        // A default here would let a new call site inherit whichever answer was written in the
        // service, silently. Required means the compiler enumerates them.
        Check("SetPassword's requireChange has no default", !parameter.HasDefaultValue);
    }

    // --- Policy -----------------------------------------------------------------------------

    private static void CheckPasswordPolicy()
    {
        var minimum = AuthenticationService.MinimumPasswordLength;

        Check("an empty password is refused",
            Policy("someone", string.Empty) == AccountOperationResult.PasswordRequired);

        Check($"a password shorter than {minimum} is refused",
            Policy("someone", new string('a', minimum - 1)) == AccountOperationResult.PasswordTooShort);

        Check($"a password of exactly {minimum} is accepted",
            Policy("someone", new string('a', minimum)) == AccountOperationResult.Success);

        // The rule the whole release rests on. Without it the answer to "replace admin/admin" is to
        // type admin again, and the forced change has moved the problem by one dialog.
        Check("the password may not be the user name",
            Policy("administrator", "administrator") == AccountOperationResult.PasswordSameAsUserName);

        Check("the user-name rule ignores case",
            Policy("Administrator", "aDMINISTRATOR") == AccountOperationResult.PasswordSameAsUserName);

        Check("the user-name rule ignores surrounding space",
            Policy("administrator", "  administrator  ") == AccountOperationResult.PasswordSameAsUserName);

        Check("an ordinary password is accepted",
            Policy("someone", GoodPassword) == AccountOperationResult.Success);
    }

    // --- The upgrade ------------------------------------------------------------------------

    private static void CheckArmShippedPasswords()
    {
        var file = new CredentialFile();

        var stillDefault = CreateRecord("manager", "manager", isAdministrator: false);
        stillDefault.MustChangePassword = false;

        var alreadyChanged = CreateRecord("staff", GoodPassword, isAdministrator: false);
        alreadyChanged.MustChangePassword = false;

        var neverShipped = CreateRecord("tina", "tina", isAdministrator: false);
        neverShipped.MustChangePassword = false;

        var administrator = CreateRecord("admin", "admin", isAdministrator: true);
        administrator.MustChangePassword = false;

        file.Users.AddRange(new[] { stillDefault, alreadyChanged, neverShipped, administrator });

        typeof(AuthenticationService)
            .GetMethod("ArmShippedPasswords", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { file });

        Check("an account still on its shipped password is armed", stillDefault.MustChangePassword);
        Check("the administrator still on admin/admin is armed", administrator.MustChangePassword);

        // The half that stops this being a support call. A year-old installation may well have given
        // 'staff' a real password months ago, and demanding a change from somebody who already did
        // the right thing is a worse outcome than the flag being slightly conservative.
        Check("an account whose password was already changed is left alone",
            !alreadyChanged.MustChangePassword);

        // Guards the difference between checking the NAME and checking the PASSWORD: 'tina' happens
        // to use her own name as her password, which is careless but is not something this product
        // published, and it is not the upgrade's business.
        Check("an account this product never seeded is left alone", !neverShipped.MustChangePassword);
    }

    // --- The gate ---------------------------------------------------------------------------

    private static void CheckSignInRefusesAnUnchangedPassword()
    {
        var service = Throwaway();
        var file = InMemoryFile(service);

        var owing = CreateRecord("fixture-owing", GoodPassword, isAdministrator: false);
        var settled = CreateRecord("fixture-settled", GoodPassword, isAdministrator: false);
        settled.MustChangePassword = false;

        file.Users.Add(owing);
        file.Users.Add(settled);

        var refused = service.Authenticate("fixture-owing", GoodPassword);

        Check("a correct password on an account owing a change does not open a session",
            refused.User is null);
        Check("...and says so, rather than reporting a wrong password",
            refused.Failure == SignInFailure.PasswordChangeRequired);

        var wrong = service.Authenticate("fixture-owing", "not-the-password");
        Check("a wrong password on the same account is still just a wrong password",
            wrong.Failure == SignInFailure.InvalidCredentials);

        var accepted = service.Authenticate("fixture-settled", GoodPassword);
        Check("an account that owes nothing signs in", accepted.User is not null);

        // Refusing the current password is what makes ChangeOwnPassword safe to expose without a
        // session: knowing it is exactly the credential Authenticate asks for, and nothing less.
        var impostor = service.ChangeOwnPassword("fixture-owing", "not-the-password", SecondGoodPassword);
        Check("changing a password without the current one is refused",
            impostor == AccountOperationResult.NotFound);

        var weak = service.ChangeOwnPassword("fixture-owing", GoodPassword, "fixture-owing");
        Check("the policy applies to the forced change too",
            weak == AccountOperationResult.PasswordSameAsUserName);

        Check("the account still owes a change after a refused attempt", owing.MustChangePassword);
    }

    // --- The one writing section ------------------------------------------------------------

    private static void CheckOwnPasswordChange(string credentials)
    {
        var original = File.Exists(credentials) ? File.ReadAllBytes(credentials) : null;
        var backup = credentials + ".authcheck.bak";

        if (original is not null)
            File.WriteAllBytes(backup, original);

        try
        {
            var service = Throwaway();
            var file = InMemoryFile(service);

            var owing = CreateRecord("fixture-owing", GoodPassword, isAdministrator: false);
            file.Users.Add(owing);

            var changed = service.ChangeOwnPassword("fixture-owing", GoodPassword, SecondGoodPassword);

            Check("a valid self-service change succeeds", changed == AccountOperationResult.Success);
            Check("...and clears the debt", !owing.MustChangePassword);
            Check("...and the new password signs in",
                service.Authenticate("fixture-owing", SecondGoodPassword).User is not null);
            Check("...and the old one no longer does",
                service.Authenticate("fixture-owing", GoodPassword).Failure
                    == SignInFailure.InvalidCredentials);

            // The opposite direction: somebody ELSE setting the password re-arms it, because they
            // have just read it out to the person it belongs to.
            var reset = service.SetPassword("fixture-owing", GoodPassword, requireChange: true);
            Check("an administrative reset succeeds", reset == AccountOperationResult.Success);
            Check("...and puts the debt back", owing.MustChangePassword);
        }
        finally
        {
            if (original is not null)
            {
                File.WriteAllBytes(credentials, original);
                try { File.Delete(backup); } catch (IOException) { /* left for the user on purpose */ }
            }
        }
    }

    // --- Plumbing ---------------------------------------------------------------------------

    private static AccountOperationResult Policy(string userName, string password)
        => (AccountOperationResult)typeof(AuthenticationService)
            .GetMethod("CheckPassword", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { userName, password })!;

    private static CredentialRecord CreateRecord(string userName, string password, bool isAdministrator)
        => (CredentialRecord)typeof(AuthenticationService)
            .GetMethod("CreateRecord", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { userName, password, isAdministrator })!;

    /// <summary>
    /// A second <see cref="AuthenticationService"/> over the same file, discarded at the end of the
    /// section. It loads the real accounts — there is nowhere else to load from — and the fixtures
    /// are pushed into its in-memory copy, so the user's own records travel along untouched and are
    /// written back unchanged if the section saves at all.
    /// </summary>
    private static AuthenticationService Throwaway()
        => (AuthenticationService)Activator.CreateInstance(typeof(AuthenticationService), nonPublic: true)!;

    private static CredentialFile InMemoryFile(AuthenticationService service)
        => (CredentialFile)typeof(AuthenticationService)
            .GetField("_file", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(service)!;

    private static T Field<T>(string name)
        => (T)typeof(AuthenticationService)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

    private static string HashOf(string path)
    {
        if (!File.Exists(path))
            return "(absent)";

        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
