using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the subscriber from the JWT's name claim against the identity store, so a token issued
/// for an account that has since been removed cannot be used to subscribe.
/// </summary>
public class SubscriberResolver : ISubscriberResolver
{
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriberResolver(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<SubscriberIdentity?> ResolveAsync(
        ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        var userName = principal.Identity?.Name ?? principal.FindFirstValue(ClaimTypes.Name);

        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await _userManager.FindByNameAsync(userName);

        if (user is null)
        {
            return null;
        }

        var email = string.IsNullOrWhiteSpace(user.Email) ? user.UserName : user.Email;

        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        return SubscriberIdentity.FromAccount(user.Id, email);
    }
}
