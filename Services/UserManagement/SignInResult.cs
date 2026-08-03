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
/// <summary>Why a sign-in was refused.</summary>
public enum SignInFailure
{
    None,

    /// <summary>Unknown user name OR wrong password — never distinguished, by design.</summary>
    InvalidCredentials,

    /// <summary>The credential was correct, but every shop this account belongs to has deactivated it.</summary>
    Deactivated,

    /// <summary>
    /// The credential was correct and the account is in good standing, but the password it was
    /// created with has to be replaced before it opens a session. Not an error to apologise for —
    /// the caller's job is to collect a new password, not to report a failure.
    /// </summary>
    PasswordChangeRequired
}

/// <summary>Outcome of a sign-in attempt.</summary>
public readonly record struct SignInResult(UserAccount? User, SignInFailure Failure)
{
    public static SignInResult Succeeded(UserAccount user) => new(user, SignInFailure.None);

    public static SignInResult Failed(SignInFailure failure) => new(null, failure);
}
