using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the billing identity of the caller from the bearer token. The shopper is never taken from the
/// request body — only from the validated token — so one authenticated caller cannot act as another.
/// </summary>
internal static class SubscriptionCaller
{
    public static bool TryResolve(ClaimsPrincipal? principal, out BillingCustomerIdentity identity)
    {
        identity = default!;

        var userName = principal?.Identity?.Name ?? principal?.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName)) return false;

        identity = BillingCustomerIdentity.ForUser(userName!);
        return true;
    }
}
