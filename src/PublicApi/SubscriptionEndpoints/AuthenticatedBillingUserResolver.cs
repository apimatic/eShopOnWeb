using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class AuthenticatedBillingUserResolver
{
    public static async Task<BillingUser?> ResolveAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager)
    {
        ApplicationUser? applicationUser = null;
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrWhiteSpace(userId))
        {
            applicationUser = await userManager.FindByIdAsync(userId);
        }

        if (applicationUser is null && !string.IsNullOrWhiteSpace(principal.Identity?.Name))
        {
            applicationUser = await userManager.FindByNameAsync(principal.Identity.Name);
        }

        var email = applicationUser?.Email ?? applicationUser?.UserName;
        return applicationUser is null || string.IsNullOrWhiteSpace(email)
            ? null
            : new BillingUser(applicationUser.Id, email);
    }
}
