using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the authenticated eShopOnWeb user from the JWT bearer token.
/// </summary>
public static class BuyerResolver
{
    public static async Task<(string BuyerId, string Email)?> ResolveAsync(
        ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var username = principal.Identity?.Name;
        if (string.IsNullOrEmpty(username))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(username);
        if (user == null)
        {
            return null;
        }

        return (user.Id, user.Email ?? username);
    }
}
