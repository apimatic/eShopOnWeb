using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi;

internal static class ShopperIdentityFactory
{
    public static ShopperIdentity FromUser(ApplicationUser user)
    {
        var email = user.Email ?? user.UserName ?? "shopper@eshop.local";
        var localPart = email.Split('@')[0];
        if (string.IsNullOrWhiteSpace(localPart))
        {
            localPart = "Shopper";
        }

        return new ShopperIdentity(
            Reference: user.UserName ?? user.Id,
            Email: email,
            FirstName: localPart,
            LastName: "eShopOnWeb");
    }
}
