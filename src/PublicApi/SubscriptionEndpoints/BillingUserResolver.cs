using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class BillingUserResolver
{
    /// <summary>
    /// Resolves the caller from the JWT: the token carries the user name (see IdentityTokenClaimService),
    /// and the identity store supplies the user id local billing records are keyed by.
    /// </summary>
    public static async Task<BillingUser?> ResolveAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return null;
        }

        // The user name is the unique login and survives re-seeding of the identity store, unlike the
        // generated user id, so it is what the provider-side customer is found by.
        var customerReference = "eshop-" + (user.NormalizedUserName ?? userName.ToUpperInvariant()).ToLowerInvariant();
        return new BillingUser(user.Id, customerReference, user.Email ?? user.UserName ?? userName);
    }
}
