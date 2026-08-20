using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class ShopperIdentityFactory
{
    public static async Task<ShopperIdentity> FromUserAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new InvalidSubscriptionRequestException("The bearer token does not contain a user identity.");
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            throw new InvalidSubscriptionRequestException("The authenticated user could not be found.");
        }

        var email = user.Email ?? user.UserName ?? userName;
        return new ShopperIdentity(user.Id, email, user.UserName ?? userName);
    }
}
