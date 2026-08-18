using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Auth;

internal static class ShopperResolver
{
    public static async Task<Shopper> FromHttpContextAsync(HttpContext httpContext, UserManager<ApplicationUser> userManager)
    {
        var userName = httpContext.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new UnauthorizedAccessException("The caller is not authenticated.");
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user == null)
        {
            throw new UnauthorizedAccessException("The authenticated user could not be found.");
        }

        var email = string.IsNullOrWhiteSpace(user.Email) ? userName : user.Email;
        return new Shopper(user.Id, email, user.UserName ?? userName);
    }
}
