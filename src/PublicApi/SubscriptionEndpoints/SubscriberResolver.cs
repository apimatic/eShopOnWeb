using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the <see cref="SubscriberInfo"/> for the authenticated caller from their JWT. The caller's
/// identity always comes from the token, never from request input.
/// </summary>
internal static class SubscriberResolver
{
    public static async Task<SubscriberInfo?> ResolveAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var userName = principal.Identity?.Name ?? principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        // eShopOnWeb users have no first/last name and their user name is their email; look up the
        // record to use the canonical email when present.
        var user = await userManager.FindByNameAsync(userName);
        var email = string.IsNullOrWhiteSpace(user?.Email) ? userName : user!.Email!;

        return SubscriberInfo.FromIdentity(userName, email);
    }
}
