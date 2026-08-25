using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the JWT-authenticated caller to the shopper identity used with the billing system.
/// </summary>
public static class ShopperContext
{
    public static async Task<ShopperInfo?> ResolveAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var username = principal.Identity?.Name;
        if (string.IsNullOrEmpty(username))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(username);
        if (user is null)
        {
            return null;
        }

        var email = user.Email ?? username;
        var firstName = email.Split('@')[0];

        return new ShopperInfo(user.Id, email, firstName, "Shopper");
    }
}
