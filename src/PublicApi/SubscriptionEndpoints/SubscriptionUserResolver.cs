using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the ApplicationUser from the JWT principal: by the NameIdentifier claim when
/// present, falling back to the username claim.
/// </summary>
internal static class SubscriptionUserResolver
{
    public static async Task<ApplicationUser?> ResolveUserAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrEmpty(userId))
        {
            var userById = await userManager.FindByIdAsync(userId);
            if (userById is not null)
            {
                return userById;
            }
        }

        var userName = principal.Identity?.Name;
        if (string.IsNullOrEmpty(userName))
        {
            return null;
        }

        return await userManager.FindByNameAsync(userName);
    }
}
