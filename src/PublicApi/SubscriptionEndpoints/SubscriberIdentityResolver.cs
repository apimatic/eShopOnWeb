using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Builds the subscriber a billing call runs as, using only the authenticated caller's token. Request
/// bodies never influence whose subscriptions are read or written.
/// </summary>
public static class SubscriberIdentityResolver
{
    public static async Task<SubscriberIdentity?> ResolveAsync(ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager)
    {
        var userName = principal.Identity?.Name ?? principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(userName);

        // eShopOnWeb registers users by e-mail, so the user name doubles as the address when the
        // Identity store has no record - which happens when the API runs on the in-memory provider
        // against a token minted in an earlier run.
        var email = user?.Email ?? (userName.Contains('@') ? userName : null);
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        return new SubscriberIdentity(userName, email!);
    }
}
