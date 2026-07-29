using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the calling shopper's <see cref="SubscriberIdentity"/> from the JWT. The token
/// carries the username in <see cref="ClaimTypes.Name"/>; we look up the Identity user to
/// obtain the stable user id (used to derive the Maxio customer reference) and email.
/// </summary>
internal static class SubscriberContext
{
    public static async Task<SubscriberIdentity?> ResolveAsync(
        ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var username = principal.Identity?.Name ?? principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(username);
        if (user is null)
        {
            return null;
        }

        return new SubscriberIdentity(user.Id, user.Email ?? username);
    }
}
