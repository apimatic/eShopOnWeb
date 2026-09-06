using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriberPrincipalExtensions
{
    /// <summary>
    /// Builds the billing identity from the bearer token. The caller never supplies who they are:
    /// it comes from the validated token, so a shopper can only ever act on their own subscriptions.
    /// </summary>
    public static Subscriber? ToSubscriber(this ClaimsPrincipal? principal)
    {
        var userName = principal?.FindFirstValue(ClaimTypes.Name) ?? principal?.Identity?.Name;

        return string.IsNullOrWhiteSpace(userName) ? null : new Subscriber(userName);
    }
}
