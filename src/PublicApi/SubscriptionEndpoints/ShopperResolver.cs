using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class ShopperResolver
{
    public static async Task<Shopper?> FromHttpContextAsync(HttpContext httpContext, UserManager<ApplicationUser> userManager)
    {
        var identity = httpContext.User.Identity;
        if (identity is not { IsAuthenticated: true } || string.IsNullOrWhiteSpace(identity.Name))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(identity.Name);
        if (user is null)
        {
            return null;
        }

        var email = user.Email
                    ?? httpContext.User.FindFirstValue(ClaimTypes.Email)
                    ?? user.UserName
                    ?? identity.Name;

        return new Shopper(user.Id, email, user.UserName ?? identity.Name);
    }
}
