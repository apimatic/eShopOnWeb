using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class ShopperResolver
{
    public static async Task<ShopperBillingProfile?> ResolveAsync(UserManager<ApplicationUser> userManager, ClaimsPrincipal user)
    {
        var userName = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var applicationUser = await userManager.FindByNameAsync(userName);
        if (applicationUser is null || string.IsNullOrWhiteSpace(applicationUser.Id))
        {
            return null;
        }

        var email = applicationUser.Email ?? applicationUser.UserName ?? userName;
        return new ShopperBillingProfile(applicationUser.Id, email, applicationUser.UserName);
    }
}
