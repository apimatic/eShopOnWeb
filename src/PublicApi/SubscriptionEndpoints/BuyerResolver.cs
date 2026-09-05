using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the calling JWT's identity into the profile used to ensure/find the caller's
/// Maxio customer record. The ApplicationUser id - not the email - is used as the Maxio
/// customer reference, since it's the immutable key for the account.
/// </summary>
internal static class BuyerResolver
{
    public static async Task<MaxioCustomerProfile?> ResolveAsync(ClaimsPrincipal user, UserManager<ApplicationUser> userManager)
    {
        var userName = user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(userName))
        {
            return null;
        }

        var appUser = await userManager.FindByNameAsync(userName);
        if (appUser?.Email is null)
        {
            return null;
        }

        var localPart = appUser.Email.Split('@')[0];
        return new MaxioCustomerProfile
        {
            Reference = appUser.Id,
            Email = appUser.Email,
            FirstName = localPart,
            LastName = "eShopOnWeb Customer"
        };
    }
}
