using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class BillingUserFactory
{
    public static async Task<BillingUser?> FromPrincipalAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager)
    {
        var userName = principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var identityUser = await userManager.FindByNameAsync(userName);
        if (identityUser is null || string.IsNullOrWhiteSpace(identityUser.Email))
        {
            return null;
        }

        var stableIdentityKey = identityUser.NormalizedUserName
            ?? identityUser.UserName
            ?? identityUser.Id;
        return new BillingUser(stableIdentityKey, identityUser.Email);
    }
}
