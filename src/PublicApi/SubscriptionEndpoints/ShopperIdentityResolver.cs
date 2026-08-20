using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class ShopperIdentityResolver
{
    public static async Task<ShopperIdentity?> ResolveAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager)
    {
        ApplicationUser? user = null;
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrWhiteSpace(userId))
        {
            user = await userManager.FindByIdAsync(userId);
        }

        if (user is null && principal.Identity?.Name is { Length: > 0 } userName)
        {
            user = await userManager.FindByNameAsync(userName);
        }

        var email = user?.Email ?? user?.UserName;
        return user is null || string.IsNullOrWhiteSpace(email)
            ? null
            : new ShopperIdentity(user.Id, email);
    }
}
