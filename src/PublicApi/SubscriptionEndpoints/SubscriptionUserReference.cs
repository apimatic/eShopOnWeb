using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The stable reference that ties an eShopOnWeb identity to its billing-provider customer record:
/// the user's email / username, as decided in plan section 8.
/// </summary>
internal static class SubscriptionUserReference
{
    public static string For(ClaimsPrincipal user)
    {
        var reference = user.Identity?.Name;

        return string.IsNullOrWhiteSpace(reference)
            ? throw new BillingConfigurationException("The authenticated principal carries no user name to identify the customer by.")
            : reference;
    }
}
