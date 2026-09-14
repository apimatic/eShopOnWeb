using System.Security.Claims;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the signed-in eShopOnWeb user from the JWT and enriches it from the Identity store.
/// </summary>
public class SubscriberResolver : ISubscriberResolver
{
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriberResolver(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<Subscriber> ResolveAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        // PublicApi issues tokens carrying the user name in ClaimTypes.Name (see
        // IdentityTokenClaimService); ClaimsIdentity.Name reads exactly that claim.
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new AuthenticationException("The bearer token does not identify a user.");
        }

        var user = await _userManager.FindByNameAsync(userName);

        // In eShopOnWeb the user name is the email address. Falling back to it keeps the endpoints
        // working on a host whose Identity store is transient, where a token issued before a
        // restart still names a valid user.
        return new Subscriber(
            userId: user?.Id ?? userName,
            userName: userName,
            email: string.IsNullOrWhiteSpace(user?.Email) ? userName : user!.Email!);
    }
}
