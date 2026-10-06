using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>The caller, as identified by the JWT (name claim) and the eShop identity store.</summary>
public sealed record ShopperIdentity(string UserName, string Email)
{
    /// <summary>Returns null when the token carries no name or names a user that does not exist.</summary>
    public static async Task<ShopperIdentity?> ResolveAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user is null || string.IsNullOrWhiteSpace(user.Email))
        {
            return null;
        }
        return new ShopperIdentity(user.UserName ?? userName, user.Email);
    }
}
