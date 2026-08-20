using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionEndpointUser
{
    public static BillingUser? From(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = principal.FindFirstValue(ClaimTypes.Email) ?? principal.Identity?.Name;
        return string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(email)
            ? null
            : new BillingUser(userId, email);
    }
}
