using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class ShopperIdentityFactory
{
    public static async Task<ShopperIdentity> FromAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            throw new SubscriptionBillingException("Authentication required.", 401);
        }

        var userName = principal.Identity.Name
            ?? principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new SubscriptionBillingException("Authentication required.", 401);
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            throw new SubscriptionBillingException($"No user found for '{userName}'.", 401);
        }

        return new ShopperIdentity(user.Id, user.Email ?? userName, user.UserName);
    }
}
