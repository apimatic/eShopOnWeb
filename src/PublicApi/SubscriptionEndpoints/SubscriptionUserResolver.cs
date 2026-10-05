using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the eShopOnWeb application user from the JWT-bearer-authenticated
/// caller (the identity comes from the token, not from a request parameter).
/// </summary>
public static class SubscriptionUserResolver
{
    public static async Task<ApplicationUser?> ResolveAsync(ClaimsPrincipal user,
        UserManager<ApplicationUser> userManager)
    {
        var name = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var found = await userManager.FindByNameAsync(name);
        if (found is null)
        {
            found = await userManager.FindByEmailAsync(name);
        }
        return found;
    }
}