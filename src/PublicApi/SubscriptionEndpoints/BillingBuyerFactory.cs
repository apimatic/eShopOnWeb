using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class BillingBuyerFactory
{
    public static BillingBuyer From(ApplicationUser user)
    {
        var email = user.Email ?? user.UserName ?? "shopper@localhost";
        var local = email.Split('@')[0];
        if (string.IsNullOrWhiteSpace(local))
        {
            local = "Shopper";
        }

        return new BillingBuyer(user.Id, email, local, "Customer");
    }
}
