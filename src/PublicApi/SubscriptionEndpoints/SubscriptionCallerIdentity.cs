using System;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Derives the billing identity of the caller from their bearer token.
/// </summary>
/// <remarks>
/// eShopOnWeb issues tokens whose name claim is the shopper's user name, which is their e-mail address; an
/// explicit e-mail claim is preferred when one is present. The e-mail is the only shopper attribute the
/// billing system needs, and it is what the deterministic billing references are derived from.
/// </remarks>
public static class SubscriptionCallerIdentity
{
    public static bool TryResolve(ClaimsPrincipal? principal, out BillingCustomerIdentity identity, out string error)
    {
        identity = default!;
        error = string.Empty;

        var email = principal?.FindFirstValue(ClaimTypes.Email);

        if (string.IsNullOrWhiteSpace(email))
        {
            email = principal?.Identity?.Name;
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            error = "The access token does not identify a user.";
            return false;
        }

        if (!email.Contains('@', StringComparison.Ordinal))
        {
            error = "The signed-in account has no e-mail address, which subscription billing requires.";
            return false;
        }

        identity = BillingCustomerIdentity.FromEmail(email);
        return true;
    }
}
