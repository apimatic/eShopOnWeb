using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi;

internal static class ShopperContext
{
    public static async Task<ShopperIdentity> ResolveAsync(ClaimsPrincipal user, UserManager<ApplicationUser> userManager)
    {
        var userName = user.Identity?.Name
            ?? user.FindFirstValue(ClaimTypes.Name)
            ?? throw new BillingValidationException("The caller is not authenticated.");

        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = user.FindFirstValue(ClaimTypes.Email);

        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(email))
        {
            var appUser = await userManager.FindByNameAsync(userName)
                ?? throw new BillingValidationException($"No user found for '{userName}'.");
            userId = appUser.Id;
            email = appUser.Email ?? userName;
        }

        return new ShopperIdentity(userId, userName, email);
    }
}
