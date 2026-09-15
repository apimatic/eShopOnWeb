using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class BillingShopperResolver
{
    public static async Task<BillingShopper?> ResolveAsync(
        ClaimsPrincipal? principal,
        UserManager<ApplicationUser> userManager)
    {
        var userName = principal?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return null;
        }

        return BillingShopper.FromIdentity(user.Id, user.Email, user.UserName);
    }
}
