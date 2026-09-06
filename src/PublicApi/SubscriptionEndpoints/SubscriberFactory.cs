using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the caller of a JWT-authenticated request to the identity that gets billed.
///
/// The bearer token carries the user name, and the ASP.NET Identity user name is what the billing
/// customer's reference is derived from: it is unique per user, and unlike the generated user id it keeps
/// its value across a reseed of the identity store, so a shopper's Maxio customer survives a restart even
/// when eShopOnWeb is running against the in-memory provider.
/// </summary>
public class SubscriberFactory
{
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriberFactory(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    /// <summary>
    /// Returns the subscriber the token identifies, or null when the token names a user this instance
    /// does not know about.
    /// </summary>
    public async Task<Subscriber?> CreateAsync(ClaimsPrincipal principal)
    {
        var userName = principal.Identity?.Name;

        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await _userManager.FindByNameAsync(userName);

        if (user is null || string.IsNullOrWhiteSpace(user.UserName))
        {
            return null;
        }

        var email = string.IsNullOrWhiteSpace(user.Email) ? user.UserName! : user.Email!;

        return new Subscriber(user.UserName!, email);
    }
}
