using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriberIdentityFactory
{
    /// <summary>
    /// Builds the subscriber identity from the authenticated caller's JWT claims. The username
    /// (<see cref="ClaimTypes.Name"/>) is used as the stable Maxio customer reference; in this
    /// app the username is the user's email, which is also used as the customer email.
    /// </summary>
    public static SubscriberIdentity? FromPrincipal(ClaimsPrincipal user)
    {
        var username = user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        var email = user.FindFirstValue(ClaimTypes.Email) ?? username;
        return new SubscriberIdentity(Reference: username, Email: email);
    }
}
