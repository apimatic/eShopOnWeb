using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Turns the caller's bearer token into the subscriber the billing provider will be asked about.
/// </summary>
/// <remarks>
/// The identity always comes from the token, never from the request body, so a caller cannot
/// enroll or inspect anyone else's subscriptions.
/// </remarks>
internal static class SubscriberResolver
{
    /// <summary>
    /// Returns the subscriber for the authenticated caller, or null when the token names an
    /// account that no longer exists.
    /// </summary>
    public static async Task<Subscriber?> ResolveAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager)
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

        return new Subscriber(user.UserName ?? userName, user.Email ?? userName);
    }
}
