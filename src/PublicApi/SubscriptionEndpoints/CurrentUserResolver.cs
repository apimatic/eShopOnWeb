using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the signed-in ApplicationUser from the JWT bearer identity.
/// </summary>
internal static class CurrentUserResolver
{
    public static async Task<ApplicationUser?> GetCurrentUserAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager)
    {
        var name = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return await userManager.FindByNameAsync(name);
    }
}
