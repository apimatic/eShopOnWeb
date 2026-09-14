using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the billing identity of the caller from the bearer token — never from the request body, so a
/// caller cannot subscribe or read subscriptions on behalf of somebody else.
/// </summary>
internal static class SubscriberIdentityExtensions
{
    public static SubscriberIdentity? ToSubscriberIdentity(
        this ClaimsPrincipal? principal,
        string? firstName = null,
        string? lastName = null)
    {
        var userName = principal?.Identity?.Name
                       ?? principal?.FindFirstValue(ClaimTypes.Name)
                       ?? principal?.FindFirstValue("unique_name");

        return string.IsNullOrWhiteSpace(userName)
            ? null
            : SubscriberIdentity.ForUser(userName, firstName, lastName);
    }
}
