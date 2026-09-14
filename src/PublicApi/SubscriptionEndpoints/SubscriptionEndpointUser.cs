using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionEndpointUser
{
    public static async Task<SubscriptionUser?> ResolveAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user is null || string.IsNullOrWhiteSpace(user.Email))
        {
            return null;
        }

        return new SubscriptionUser(user.Id, user.Email, user.UserName ?? user.Email);
    }
}
