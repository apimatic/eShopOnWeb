using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the signed-in eShopOnWeb account from the JWT caller identity.
/// The JWT carries only the username (email) claim — the store is consulted for the canonical account.
/// </summary>
public static class SubscriptionUserResolver
{
    public static async Task<ApplicationUser?> ResolveAsync(ClaimsPrincipal? principal, UserManager<ApplicationUser> userManager)
    {
        var name = principal?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(name);
        return user ?? await userManager.FindByEmailAsync(name);
    }
}