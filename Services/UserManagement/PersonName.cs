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
/// <summary>
/// How a first and last name become the one string a screen shows.
/// </summary>
/// <remarks>
/// One definition, because every screen that shows a person composes it — the roster, the account
/// list, the account detail, the greeting — and a second copy is how "Tina Zhang" on one screen
/// becomes "Zhang, Tina" on the next.
///
/// KNOWN LIMITATION, recorded rather than half-solved: the parts are joined given-name-first with a
/// space, which is the English and French convention. A Chinese name is written family-name-first
/// with no separator, so a person who fills in BOTH boxes reads back in the western order. The
/// migration deliberately leaves an unsplit Chinese name whole in <c>FirstName</c>, so this only
/// affects names typed into both boxes on purpose. Making the order a language rule (the way
/// <c>Format.ListSeparator</c> is) is the fix if it ever matters.
/// </remarks>
public static class PersonName
{
    /// <summary>Both halves as one name, or an empty string when neither was filled in.</summary>
    public static string Full(string? firstName, string? lastName)
        => string.Join(' ', new[] { firstName, lastName }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim()));

    /// <summary>
    /// What to call this person on screen: their name if they have one, otherwise the account they
    /// sign in with. Never blank — a nameless row is worse than a technical one.
    /// </summary>
    public static string Label(string? firstName, string? lastName, string userName)
    {
        var full = Full(firstName, lastName);
        return full.Length == 0 ? userName : full;
    }

    /// <summary>
    /// What to GREET this person by. The first name where there is one, because "Hi Tina" is the
    /// point of a greeting and "Hi Tina Zhang" is a form letter.
    /// </summary>
    public static string Greeting(string? firstName, string? lastName, string userName)
        => string.IsNullOrWhiteSpace(firstName)
            ? Label(firstName, lastName, userName)
            : firstName.Trim();
}
