using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionEndpointUser
{
    public static async Task<ApplicationUser?> ResolveAsync(
        UserManager<ApplicationUser> userManager,
        HttpContext httpContext)
    {
        var userName = httpContext.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        return await userManager.FindByNameAsync(userName);
    }
}
