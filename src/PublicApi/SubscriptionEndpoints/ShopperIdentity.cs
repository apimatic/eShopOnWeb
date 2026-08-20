using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class ShopperIdentity
{
    public static async Task<Shopper?> ResolveAsync(HttpContext? httpContext, UserManager<ApplicationUser> userManager)
    {
        var userName = httpContext?.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return null;
        }

        var email = !string.IsNullOrWhiteSpace(user.Email) ? user.Email : userName;
        return new Shopper(user.Id, email, user.UserName ?? userName);
    }
}
