using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class BillingCurrentUser
{
    public static async Task<ApplicationUser?> GetAsync(HttpContext? httpContext, UserManager<ApplicationUser> userManager)
    {
        var userName = httpContext?.User.Identity?.Name
            ?? httpContext?.User.FindFirstValue(ClaimTypes.Name);

        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        return await userManager.FindByNameAsync(userName);
    }
}
