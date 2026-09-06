using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Turns the bearer token's principal into the shopper the billing system should be called for.
/// The identity is never taken from a request body: a caller can only ever act on their own
/// subscriptions.
/// </summary>
public interface ISubscriberIdentityAccessor
{
    Task<SubscriberIdentity?> ResolveAsync(ClaimsPrincipal principal);
}

public class SubscriberIdentityAccessor : ISubscriberIdentityAccessor
{
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriberIdentityAccessor(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<SubscriberIdentity?> ResolveAsync(ClaimsPrincipal principal)
    {
        var userName = principal?.Identity?.Name;

        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await _userManager.FindByNameAsync(userName);

        if (user is null)
        {
            return null;
        }

        // The user name - not ApplicationUser.Id - is the stable key. eShopOnWeb issues a fresh
        // identity GUID every time the in-memory identity store is re-seeded, whereas the user
        // name survives restarts, and the billing customer reference has to survive with it.
        return new SubscriberIdentity
        {
            UserId = user.UserName ?? userName,
            Email = user.Email ?? user.UserName ?? userName
        };
    }
}
