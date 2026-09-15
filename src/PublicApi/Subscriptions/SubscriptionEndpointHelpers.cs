using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

internal static class SubscriptionEndpointHelpers
{
    public static async Task<ApplicationUser?> GetCurrentUserAsync(
        HttpContext httpContext, UserManager<ApplicationUser> userManager)
    {
        var userName = httpContext.User.FindFirstValue(ClaimTypes.Name);
        return string.IsNullOrWhiteSpace(userName)
            ? null
            : await userManager.FindByNameAsync(userName);
    }
}
