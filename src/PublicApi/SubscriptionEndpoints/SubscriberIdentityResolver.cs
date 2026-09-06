using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Turns the caller's bearer token into the subscriber a billing call acts for.
/// </summary>
/// <remarks>
/// The identity comes from the token and only from the token: no route, query or body value can
/// change whose subscription is read or created. The local identity store is consulted purely to
/// enrich the record with an email address, and a caller whose row is absent is still served from
/// the token alone, because the token is the authority on who they are.
/// </remarks>
public class SubscriberIdentityResolver
{
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriberIdentityResolver(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    /// <summary>
    /// Resolves the subscriber for an authenticated principal, or <c>null</c> when the principal
    /// carries no usable name.
    /// </summary>
    public async Task<SubscriberIdentity?> ResolveAsync(ClaimsPrincipal? principal)
    {
        var userName = principal?.Identity?.Name
                       ?? principal?.FindFirstValue(ClaimTypes.Name);

        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await _userManager.FindByNameAsync(userName);

        return new SubscriberIdentity(userName, user?.Email);
    }
}
