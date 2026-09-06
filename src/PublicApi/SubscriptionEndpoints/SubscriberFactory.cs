using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Builds the billing <see cref="Subscriber"/> from the bearer token. The caller never states who
/// they are: the identity comes from the validated JWT and nothing else, so one shopper can never
/// read or change another shopper's subscriptions.
/// </summary>
public static class SubscriberFactory
{
    public static Subscriber? FromPrincipal(ClaimsPrincipal? principal, string? firstName = null, string? lastName = null)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var userName = principal.FindFirstValue(ClaimTypes.Name)
                       ?? principal.FindFirstValue("unique_name")
                       ?? principal.Identity.Name;

        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var email = principal.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue("email");

        return new Subscriber(userName, email, firstName, lastName);
    }
}
