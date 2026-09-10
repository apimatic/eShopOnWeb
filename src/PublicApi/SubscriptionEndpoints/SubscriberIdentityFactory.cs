using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Builds a <see cref="SubscriberIdentity"/> from the authenticated caller's JWT.
/// The username (the <see cref="ClaimTypes.Name"/> claim issued by the token service)
/// is used as the stable billing-system reference so "ensure customer" is idempotent.
/// </summary>
internal static class SubscriberIdentityFactory
{
    public static SubscriberIdentity? FromPrincipal(ClaimsPrincipal? principal)
    {
        var username = principal?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        // eShopOnWeb usernames are email addresses; use the username for both reference
        // and email. Split a display name off the local part purely for presentation in
        // the billing system (these fields are cosmetic there).
        var atIndex = username.IndexOf('@');
        var firstName = atIndex > 0 ? username[..atIndex] : username;

        return new SubscriberIdentity(
            Reference: username,
            Email: username,
            FirstName: firstName,
            LastName: "eShopOnWeb");
    }
}
