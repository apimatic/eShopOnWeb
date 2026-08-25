using System;
using System.Net;
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class BillingIdentity
{
    public static BillingUser FromPrincipal(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            throw new SubscriptionBillingException(HttpStatusCode.Unauthorized, "Authentication is required.");
        }

        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = principal.FindFirstValue(ClaimTypes.Email) ?? principal.Identity.Name;
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(email))
        {
            throw new SubscriptionBillingException(
                HttpStatusCode.Unauthorized,
                "The bearer token does not contain the required user identity claims.");
        }

        var localPart = email.Split('@', 2, StringSplitOptions.TrimEntries)[0];
        var firstName = principal.FindFirstValue(ClaimTypes.GivenName);
        var lastName = principal.FindFirstValue(ClaimTypes.Surname);

        return new BillingUser(
            userId,
            email,
            string.IsNullOrWhiteSpace(firstName) ? localPart : firstName,
            string.IsNullOrWhiteSpace(lastName) ? "Customer" : lastName);
    }
}
