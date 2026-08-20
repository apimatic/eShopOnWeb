using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class ShopperIdentityFactory
{
    public static ShopperIdentity FromUser(ApplicationUser user)
    {
        var email = user.Email ?? user.UserName ?? string.Empty;
        var localPart = email;
        var at = email.IndexOf('@', StringComparison.Ordinal);
        if (at > 0)
        {
            localPart = email[..at];
        }

        return new ShopperIdentity
        {
            UserId = user.Id,
            Email = string.IsNullOrWhiteSpace(email) ? $"{user.Id}@eshop.local" : email,
            FirstName = string.IsNullOrWhiteSpace(localPart) ? "Shopper" : localPart,
            LastName = "eShopOnWeb"
        };
    }
}
