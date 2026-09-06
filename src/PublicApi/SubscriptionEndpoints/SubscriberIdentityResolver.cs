using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Turns the authenticated principal into the subscriber a billing call acts on. The identity is
/// always taken from the bearer token, never from the request body, so a caller cannot subscribe
/// or read on behalf of somebody else.
/// </summary>
public class SubscriberIdentityResolver
{
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriberIdentityResolver(UserManager<ApplicationUser> userManager)
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

        // The token stays valid for days, so the identity row may be gone - it lives in the
        // in-memory database in the default local setup. The name claim is still the stable key
        // the billing customer is derived from, so fall back to it.
        var user = await _userManager.FindByNameAsync(userName);
        var email = user?.Email ?? userName;

        return new SubscriberIdentity(userName, email);
    }
}
