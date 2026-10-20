using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.SubscriptionBilling;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class BillingUserResolver
{
    /// <summary>
    /// The caller's identity comes only from the validated token (its name claim), never from the request body.
    /// Returns null when the token names no existing user.
    /// </summary>
    public static async Task<BillingUser?> ResolveAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var userName = principal.Identity?.IsAuthenticated == true ? principal.Identity.Name : null;
        if (string.IsNullOrWhiteSpace(userName))
            return null;

        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
            return null;

        var email = !string.IsNullOrWhiteSpace(user.Email) ? user.Email : userName;
        return new BillingUser(userName, email);
    }
}
