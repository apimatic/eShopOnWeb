using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Turns the bearer token's principal into the shopper the billing endpoints act on. The caller
/// never supplies their own identity: it always comes from the token.
/// </summary>
public static class SubscriberResolver
{
    /// <summary>
    /// Returns the shopper named by the token, or <see langword="null"/> when the token carries no
    /// name or names an account that no longer exists.
    /// </summary>
    public static async Task<Subscriber?> ResolveAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager,
        string? firstName = null,
        string? lastName = null)
    {
        var userName = principal.Identity?.Name ?? principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return null;
        }

        // eShopOnWeb logs users in by email, so UserName and Email are the same value; fall back to
        // the login name if an account was created without one.
        var email = string.IsNullOrWhiteSpace(user.Email) ? user.UserName! : user.Email!;

        return new Subscriber(user.UserName!, email, firstName, lastName);
    }
}
