using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Shared helpers for the subscription endpoints.</summary>
internal static class SubscriptionUserResolver
{
    /// <summary>
    /// Resolves the authenticated eShop user from the JWT subject (ClaimTypes.Name = user name).
    /// Returns null when the token does not carry a known eShop account.
    /// </summary>
    public static Task<ApplicationUser?> GetAuthenticatedUserAsync(ClaimsPrincipal user,
        UserManager<ApplicationUser> userManager)
    {
        var userName = user.Identity?.Name;
        return string.IsNullOrWhiteSpace(userName)
            ? Task.FromResult<ApplicationUser?>(null)
            : userManager.FindByNameAsync(userName);
    }
}
