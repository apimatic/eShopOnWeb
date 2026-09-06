using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the authenticated caller into the shopper billing operates on.
/// </summary>
/// <remarks>
/// The identity comes from the bearer token and nowhere else, so a caller cannot name someone else's
/// account in a request body and act on it. The token carries the user name; the email is read back from
/// Identity rather than assumed equal to it.
/// </remarks>
public class SubscriberResolver
{
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriberResolver(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    /// <summary>
    /// Returns the shopper the token identifies, or <c>null</c> when that user no longer exists or has no
    /// email address — either of which makes billing on their behalf impossible.
    /// </summary>
    public async Task<Subscriber?> ResolveAsync(ClaimsPrincipal caller)
    {
        var userName = caller.Identity?.Name;

        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await _userManager.FindByNameAsync(userName);
        var email = user?.Email;

        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        return new Subscriber(email);
    }
}
