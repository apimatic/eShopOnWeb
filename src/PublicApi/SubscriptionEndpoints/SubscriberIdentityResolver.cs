using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Turns the bearer token's principal into the shopper a billing call acts on behalf of.
/// </summary>
/// <remarks>
/// The caller never names the subscriber: identity comes from the token only, so one shopper can
/// never read or alter another's subscriptions.
/// </remarks>
internal static class SubscriberIdentityResolver
{
    /// <summary>
    /// Returns null when the token carries no usable name, or names a user that no longer exists.
    /// </summary>
    public static async Task<SubscriberIdentity?> ResolveAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager,
        string? firstName = null,
        string? lastName = null)
    {
        var userName = principal.Identity?.Name
                       ?? principal.FindFirstValue(ClaimTypes.Name)
                       ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return null;
        }

        return new SubscriberIdentity(
            UserName: user.UserName ?? userName,
            Email: user.Email,
            FirstName: firstName,
            LastName: lastName,
            UserId: user.Id).Validated();
    }
}
