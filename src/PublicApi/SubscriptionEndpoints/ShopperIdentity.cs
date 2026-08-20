using System;
using System.Linq;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class ShopperIdentity
{
    public static (string FirstName, string LastName) SplitDisplayName(ApplicationUser user)
    {
        var source = user.Email ?? user.UserName ?? "shopper";
        var local = source.Split('@')[0];
        var parts = local.Split(new[] { '.', '-', '_', '+' }, StringSplitOptions.RemoveEmptyEntries);
        var first = parts.Length > 0 ? parts[0] : "Shopper";
        var last = parts.Length > 1 ? parts[1] : "Customer";
        return (first, last);
    }
}
