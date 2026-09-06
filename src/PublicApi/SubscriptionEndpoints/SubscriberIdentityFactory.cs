using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Derives the billing subscriber from the bearer token. The caller never gets to say who they are:
/// the identity always comes from the validated JWT, so a shopper can only ever subscribe or read on
/// their own behalf.
/// </summary>
public static class SubscriberIdentityFactory
{
    private static readonly string[] NameClaimTypes =
    {
        ClaimTypes.Name,
        "unique_name",
        "name",
        ClaimTypes.Email,
        "email",
        ClaimTypes.NameIdentifier,
        "sub"
    };

    /// <summary>
    /// Returns the subscriber for the authenticated principal, or null when the token carries no
    /// usable name claim.
    /// </summary>
    public static SubscriberIdentity? From(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var userName = NameClaimTypes
            .Select(principal.FindFirstValue)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        // eShopOnWeb signs in with the e-mail address as the user name, so the two coincide; the
        // e-mail claim is preferred when a token happens to carry both.
        var email = principal.FindFirstValue(ClaimTypes.Email)
                    ?? principal.FindFirstValue("email")
                    ?? userName;

        return new SubscriberIdentity(userName!, email);
    }
}
