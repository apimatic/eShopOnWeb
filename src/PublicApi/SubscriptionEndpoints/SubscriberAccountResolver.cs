using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Turns the caller's bearer token into the account that subscriptions are billed to.
/// </summary>
public class SubscriberAccountResolver
{
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriberAccountResolver(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    /// <summary>
    /// Resolves the authenticated principal to a <see cref="SubscriberAccount"/>, or <c>null</c> when
    /// the token does not correspond to a user of this application.
    /// </summary>
    public async Task<SubscriberAccount?> ResolveAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        var userName = principal.FindFirstValue(ClaimTypes.Name) ?? principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return null;
        }

        var email = user.Email ?? userName;

        // The billing customer is keyed on the account key, so the key has to be stable for the life of
        // the account. The Identity user id is not usable here: eShopOnWeb issues a fresh id every time
        // the in-memory identity store is seeded, which is on every restart. The email address is the
        // account's durable business identity and is what the shopper signs in with, so it is
        // normalised to lower case and used instead.
        var accountKey = email.Trim().ToLowerInvariant();

        return new SubscriberAccount(accountKey, email);
    }
}
