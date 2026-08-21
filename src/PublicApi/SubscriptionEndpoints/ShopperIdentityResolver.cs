using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class ShopperIdentityResolver
{
    public static async Task<ShopperIdentity?> FromAsync(ClaimsPrincipal? principal, UserManager<ApplicationUser>? users)
    {
        if (principal?.Identity?.IsAuthenticated != true || users is null)
        {
            return null;
        }

        var userName = principal.Identity.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await users.FindByNameAsync(userName);
        if (user is null)
        {
            return null;
        }

        var email = user.Email ?? user.UserName ?? user.Id;
        return new ShopperIdentity(user.Id, email, user.UserName);
    }
}
