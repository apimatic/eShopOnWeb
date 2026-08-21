using System;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionIdentity
{
    public static BillingCustomerIdentity From(ApplicationUser user)
    {
        var email = user.Email ?? user.UserName ?? throw new InvalidOperationException(
            "The authenticated user does not have an email address.");
        var localPart = email.Split('@', 2, StringSplitOptions.RemoveEmptyEntries)[0];
        var firstName = string.IsNullOrWhiteSpace(localPart) ? "eShop" : localPart;

        // The JWT identifies callers by username. Using its normalized value keeps Maxio
        // references stable even when the local in-memory Identity database is reseeded.
        var stableUserId = user.NormalizedUserName ?? user.UserName ?? email;
        return new BillingCustomerIdentity(stableUserId, email, firstName, "Customer");
    }
}
