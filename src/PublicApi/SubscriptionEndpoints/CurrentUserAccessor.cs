using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the eShopOnWeb identity behind the caller's JWT. The token only carries a
/// <see cref="ClaimTypes.Name"/> claim (see AuthenticateEndpoint), so the ApplicationUser
/// record is looked up by username to get a stable id for use as the Maxio customer reference.
/// </summary>
internal static class CurrentUserAccessor
{
    public static async Task<ApplicationUser?> GetCurrentUserAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var userName = principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(userName))
        {
            return null;
        }

        return await userManager.FindByNameAsync(userName);
    }

    public static MaxioCustomerProfile ToCustomerProfile(ApplicationUser user)
    {
        var email = user.Email ?? user.UserName ?? user.Id;
        var atIndex = email.IndexOf('@');
        var firstName = atIndex > 0 ? email[..atIndex] : email;

        return new MaxioCustomerProfile(user.Id, email, firstName, "eShopOnWeb Customer");
    }
}
