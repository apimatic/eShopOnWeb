using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class BillingIdentityFactory
{
    public static BillingIdentity Create(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var firstName = principal.FindFirstValue(ClaimTypes.GivenName);
        var lastName = principal.FindFirstValue(ClaimTypes.Surname);
        var email = principal.FindFirstValue(ClaimTypes.Email);

        if (string.IsNullOrWhiteSpace(userId) ||
            string.IsNullOrWhiteSpace(firstName) ||
            string.IsNullOrWhiteSpace(lastName) ||
            string.IsNullOrWhiteSpace(email))
        {
            throw new BillingException(
                BillingFailureKind.Authentication,
                "The access token does not contain a complete billing identity. Sign in again before subscribing.");
        }

        return new BillingIdentity(userId, firstName, lastName, email);
    }
}
