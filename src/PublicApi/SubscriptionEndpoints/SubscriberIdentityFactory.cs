using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Builds the billing subscriber from the caller's bearer token. The identity is never taken from
/// the request body, so one shopper cannot subscribe, or read subscriptions, on behalf of another.
/// </summary>
internal static class SubscriberIdentityFactory
{
    public static SubscriberIdentity? FromPrincipal(
        ClaimsPrincipal? principal,
        string? firstName = null,
        string? lastName = null,
        string? organization = null)
    {
        var userName = principal?.FindFirstValue(ClaimTypes.Name) ?? principal?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        // eShopOnWeb user names are email addresses; prefer an explicit email claim when present.
        var email = principal?.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email))
        {
            email = userName;
        }

        return new SubscriberIdentity(userName.Trim(), email.Trim(), firstName, lastName, organization);
    }
}
