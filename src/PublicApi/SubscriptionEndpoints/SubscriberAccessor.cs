using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.SubscriptionBilling;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the authenticated shopper's identity to the <see cref="SubscriberAccount"/> used to
/// correlate them with their Maxio customer.
/// </summary>
internal static class SubscriberAccessor
{
    /// <summary>
    /// Returns the shopper for the current principal, or <c>null</c> when the token does not
    /// carry a username or the backing user no longer exists.
    /// </summary>
    public static async Task<SubscriberAccount?> ResolveAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var applicationUser = await userManager.FindByNameAsync(userName);
        if (applicationUser == null)
        {
            return null;
        }

        var email = !string.IsNullOrWhiteSpace(applicationUser.Email)
            ? applicationUser.Email
            : applicationUser.UserName ?? userName;

        return new SubscriberAccount(applicationUser.Id, email);
    }
}
