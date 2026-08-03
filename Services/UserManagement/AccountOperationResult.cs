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
    Protected,

    /// <summary>Shorter than <see cref="AuthenticationService.MinimumPasswordLength"/>.</summary>
    PasswordTooShort,

    /// <summary>The password is the user name, which is how this product used to ship accounts.</summary>
    PasswordSameAsUserName,

    /// <summary>Every shop this account belongs to has delisted it, so it cannot be signed in to.</summary>
    Deactivated
}
