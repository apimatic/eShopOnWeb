using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Turns the authenticated caller into the shopper a billing customer is created for.
/// <para>
/// The identity always comes from the bearer token and is then confirmed against the identity
/// store - never from request input - so a caller cannot enroll, or read the subscriptions of,
/// somebody else.
/// </para>
/// </summary>
public static class SubscriberIdentityResolver
{
    public static async Task<SubscriberIdentity?> ResolveAsync(ClaimsPrincipal? principal, UserManager<ApplicationUser> userManager)
    {
        var userName = principal?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user?.UserName is null)
        {
            // A well-formed token for an account that no longer exists.
            return null;
        }

        // eShopOnWeb registers shoppers with their email address as the user name, so the user
        // name is the sensible fallback when the profile has no email recorded.
        var email = string.IsNullOrWhiteSpace(user.Email) ? user.UserName : user.Email;

        return new SubscriberIdentity(user.UserName, email);
    }
}
