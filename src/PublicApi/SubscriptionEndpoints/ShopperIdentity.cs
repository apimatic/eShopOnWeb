using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class ShopperIdentity
{
    public static async Task<ShopperProfile> ResolveAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new BillingValidationException("The access token does not identify a shopper.");
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            throw new BillingValidationException($"No eShopOnWeb user matches '{userName}'.");
        }

        return new ShopperProfile
        {
            UserId = user.Id,
            Email = user.Email ?? user.UserName ?? string.Empty,
            UserName = user.UserName ?? userName
        };
    }
}
