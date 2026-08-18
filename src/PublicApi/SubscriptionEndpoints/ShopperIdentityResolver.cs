using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class ShopperIdentityResolver
{
    public static async Task<ShopperIdentity?> ResolveAsync(HttpContext httpContext, UserManager<ApplicationUser> userManager)
    {
        var userName = httpContext.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user is null || string.IsNullOrWhiteSpace(user.Id))
        {
            return null;
        }

        var email = !string.IsNullOrWhiteSpace(user.Email) ? user.Email : userName;
        var (firstName, lastName) = ShopperName.FromUser(email, user.UserName);
        return new ShopperIdentity(user.Id, email, firstName, lastName);
    }
}
