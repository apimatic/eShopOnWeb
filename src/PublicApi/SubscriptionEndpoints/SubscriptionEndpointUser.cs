using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionEndpointUser
{
    public static async Task<ApplicationUser?> FindAsync(HttpContext context, UserManager<ApplicationUser> userManager)
    {
        var username = context.User.Identity?.Name;
        return string.IsNullOrWhiteSpace(username) ? null : await userManager.FindByNameAsync(username);
    }
}
