using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Derives the billing <see cref="SubscriberIdentity"/> from the authenticated caller. The
/// identity always comes from the JWT (never request input), so a shopper can only act on
/// their own subscriptions.
/// </summary>
internal static class SubscriberIdentityResolver
{
    public static SubscriberIdentity? Resolve(ClaimsPrincipal? user)
    {
        // eShopOnWeb issues the token with ClaimTypes.Name set to the user name (email).
        var userName = user?.Identity?.Name
            ?? user?.FindFirstValue(ClaimTypes.Name)
            ?? user?.FindFirstValue(ClaimTypes.Email);

        return string.IsNullOrWhiteSpace(userName) ? null : new SubscriberIdentity(userName);
    }
}
