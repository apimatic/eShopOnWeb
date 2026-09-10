using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Builds a <see cref="SubscriberIdentity"/> from the authenticated caller's JWT. The caller's
/// identity is taken from the token (never from the request body), so a user can only ever act on
/// their own subscriptions.
/// </summary>
internal static class SubscriberIdentityFactory
{
    public static SubscriberIdentity FromPrincipal(ClaimsPrincipal principal)
    {
        // eShopOnWeb issues the username (email) as the name claim on the JWT.
        var email = principal.FindFirstValue(ClaimTypes.Name) ?? principal.Identity?.Name;
        return SubscriberIdentity.FromEmail(email!);
    }
}
