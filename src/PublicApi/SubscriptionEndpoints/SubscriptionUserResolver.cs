using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the identity from the JWT bearer token to the local user, whose
/// stable id is used as the billing-system customer reference.
/// </summary>
public static class SubscriptionUserResolver
{
    public static async Task<(string UserId, string UserName, string Email)?> ResolveAsync(
        ClaimsPrincipal user, UserManager<ApplicationUser> userManager)
    {
        var userName = user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(userName))
        {
            return null;
        }

        var applicationUser = await userManager.FindByNameAsync(userName);
        if (applicationUser == null)
        {
            return null;
        }

        return (applicationUser.Id, userName, applicationUser.Email ?? userName);
    }
}
