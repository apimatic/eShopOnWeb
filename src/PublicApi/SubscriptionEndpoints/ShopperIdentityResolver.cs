using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class ShopperIdentityResolver
{
    public static async Task<ShopperIdentity?> FromHttpContextAsync(
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager)
    {
        var principal = httpContext.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            var userName = principal.Identity.Name;
            if (!string.IsNullOrWhiteSpace(userName))
            {
                user = await userManager.FindByNameAsync(userName);
            }
        }

        if (user is null)
        {
            return null;
        }

        var email = user.Email
                    ?? principal.FindFirstValue(ClaimTypes.Email)
                    ?? user.UserName
                    ?? string.Empty;

        return new ShopperIdentity(user.Id, email, user.UserName);
    }
}
