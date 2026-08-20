using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi;

internal static class ShopperResolver
{
    public static async Task<Shopper?> ResolveAsync(HttpContext httpContext, UserManager<ApplicationUser> userManager)
    {
        var userName = httpContext.User.Identity?.Name
            ?? httpContext.User.FindFirstValue(ClaimTypes.Name);

        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user is null || string.IsNullOrWhiteSpace(user.Id))
        {
            return null;
        }

        var email = user.Email ?? user.UserName ?? userName;
        return new Shopper(user.Id, email, user.UserName ?? userName);
    }
}
