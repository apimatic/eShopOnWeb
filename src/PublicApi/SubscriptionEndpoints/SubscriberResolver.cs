using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the authenticated caller (from the JWT) into a <see cref="SubscriberIdentity"/>.
/// The stable <see cref="ApplicationUser.Id"/> is used as the billing customer reference so the
/// mapping survives restarts without local persistence.
/// </summary>
public sealed class SubscriberResolver
{
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriberResolver(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    /// <summary>Returns the subscriber for the principal, or null when the user cannot be found.</summary>
    public async Task<SubscriberIdentity?> ResolveAsync(ClaimsPrincipal principal)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return null;
        }

        return new SubscriberIdentity(
            UserId: user.Id,
            Email: user.Email ?? userName,
            FirstName: null,
            LastName: null);
    }
}
