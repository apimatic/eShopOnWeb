using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Turns the bearer token's principal into the shopper the subscription endpoints act for.
/// </summary>
/// <remarks>
/// The token carries only a user name, which is also eShopOnWeb's buyer identity, so the Identity
/// store is consulted only for the shopper's email and user id. Nothing about the subscriber is
/// ever taken from the request body: a caller can only ever subscribe themselves.
/// </remarks>
public class SubscriberIdentityResolver
{
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriberIdentityResolver(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    /// <summary>Returns the caller's subscriber identity, or null when the token names no known user.</summary>
    public async Task<SubscriberIdentity?> ResolveAsync(ClaimsPrincipal? principal)
    {
        var userName = principal?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName)) return null;

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null) return null;

        // eShopOnWeb seeds users with their email as the user name; fall back to it so a billing
        // customer always gets a deliverable address.
        var email = string.IsNullOrWhiteSpace(user.Email) ? userName : user.Email!;

        return new SubscriberIdentity(userName, email, user.Id);
    }
}
