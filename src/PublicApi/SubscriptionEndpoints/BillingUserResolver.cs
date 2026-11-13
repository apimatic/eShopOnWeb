using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the billing identity from the JWT: the token's name claim (what <c>IdentityTokenClaimService</c>
/// issues) must name an existing eShopOnWeb user. Nothing in the request body can choose the identity.
/// </summary>
public static class BillingUserResolver
{
    public static async Task<BillingUser?> ResolveAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var userName = principal.Identity?.IsAuthenticated == true ? principal.Identity.Name : null;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user?.UserName is null)
        {
            return null;
        }

        return new BillingUser(user.UserName, string.IsNullOrWhiteSpace(user.Email) ? user.UserName : user.Email);
    }
}
