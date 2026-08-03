using CameywareOrder.Models;
using CameywareOrder.Services;

namespace CameywareOrder.GraphQL;

/// <summary>
/// The gate every resolver passes through before it touches an order.
/// </summary>
/// <remarks>
/// Until v9.3.0 there was none. The schema exposed read, create, update and delete over HTTP with no
/// authentication and no capability check, and the host registered no authorization middleware — so
/// a request that reached the port could do anything the application could, whoever was signed in and
/// whether or not anybody was. The API is now off by default as well
/// (<see cref="IntegrationSettingsStore"/>); this is the other half, and the half that matters when
/// somebody turns it on.
///
/// **The API acts as the signed-in session, not as a service account.** That is deliberate and it is
/// the honest model for an in-process server inside a desktop application: there is exactly one
/// session, it is the person standing at the till, and orders are already scoped to the shop they
/// have open by <c>AppDbContext</c>'s query filter. Anything else would mean inventing a second
/// identity system — API keys, their storage, their rotation — for a feature no screen in the
/// product consumes.
///
/// The consequence is worth stating plainly: with nobody signed in, the API answers nothing. A
/// caller that wants data must wait for the shop to open, which is the same rule the shop's own
/// staff work under.
///
/// Messages are English and are not routed through the string table. They are read by whoever wrote
/// the integration, in a JSON error body — not by a shop, on a screen.
/// </remarks>
internal static class ApiAuthorization
{
    /// <summary>
    /// Throws unless somebody is signed in AND holds <paramref name="capability"/>.
    /// </summary>
    /// <remarks>
    /// The two failures are reported separately on purpose. "Nobody is signed in" is a state the
    /// caller can wait out; "this account may not do that" is one they cannot, and an integration
    /// that could not tell them apart would retry forever against a permission it will never be
    /// granted.
    /// </remarks>
    public static void Require(AppCapability capability)
    {
        var authentication = AuthenticationService.Instance;

        if (authentication.CurrentUser is null)
        {
            throw new GraphQLException(
                "No user is signed in. The API acts as the signed-in session, so it answers nothing "
                + "until somebody has opened a shop in the application.");
        }

        if (!authentication.Can(capability))
        {
            throw new GraphQLException(
                $"The signed-in account does not hold the '{capability}' permission.");
        }
    }
}
