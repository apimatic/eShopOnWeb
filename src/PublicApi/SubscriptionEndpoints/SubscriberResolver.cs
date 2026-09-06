using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <inheritdoc />
public class SubscriberResolver : ISubscriberResolver
{
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriberResolver(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<Subscriber?> ResolveAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        // PublicApi tokens carry the user name (an email address in eShopOnWeb) as the name claim.
        var userName = principal.Identity?.Name ?? principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await _userManager.FindByNameAsync(userName);

        // The subscriber key is derived from the normalised user name, not from the Identity row's
        // primary key. The key is written into Maxio as the customer reference and has to survive
        // for as long as the account does - but eShopOnWeb assigns Identity ids at seed time, so
        // they are regenerated whenever the store is rebuilt (which is every restart when running
        // on the in-memory provider). The normalised user name is unique, is what the token
        // asserts, and is stable across those rebuilds.
        var key = (user?.NormalizedUserName ?? _userManager.NormalizeName(userName)).ToLowerInvariant();
        var email = user?.Email ?? userName;

        return new Subscriber(key, email);
    }
}
