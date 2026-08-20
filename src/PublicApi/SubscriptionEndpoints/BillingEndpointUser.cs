using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class BillingEndpointUser
{
    public static async Task<BillingUser?> ResolveAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var applicationUser = await userManager.FindByNameAsync(userName);
        if (applicationUser is null)
        {
            return null;
        }

        var email = applicationUser.Email ?? applicationUser.UserName;
        var stableUserId = applicationUser.NormalizedUserName ?? applicationUser.UserName?.ToUpperInvariant();
        return string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(stableUserId)
            ? null
            : new BillingUser(stableUserId, email);
    }
}
