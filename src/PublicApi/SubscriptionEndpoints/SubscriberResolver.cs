using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Builds the <see cref="SubscriberIdentity"/> for the authenticated caller. Identity always comes
/// from the JWT principal (never request input), so a shopper can only act on their own account.
/// </summary>
public static class SubscriberResolver
{
    public static async Task<SubscriberIdentity> ResolveAsync(
        ClaimsPrincipal? principal, UserManager<ApplicationUser> userManager)
    {
        var userName = principal?.Identity?.Name ?? principal?.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new System.UnauthorizedAccessException("The request is not associated with an authenticated user.");
        }

        // In eShopOnWeb the username is the email. Resolve the user to obtain the canonical email
        // and a stable user id; fall back to the claim if the user record is unavailable.
        var user = await userManager.FindByNameAsync(userName);
        var email = user?.Email ?? userName;
        var userId = user?.Id ?? userName;
        return new SubscriberIdentity(userId, email);
    }
}
