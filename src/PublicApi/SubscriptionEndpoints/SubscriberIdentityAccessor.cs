using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Turns the authenticated principal into the shopper identity handed to the billing service.
/// </summary>
/// <remarks>
/// The login name comes from the bearer token and the email from the eShopOnWeb user record.
/// Neither is ever read from the request body, so a caller cannot bill against — or read the
/// subscriptions of — anyone but themselves.
/// </remarks>
public class SubscriberIdentityAccessor
{
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriberIdentityAccessor(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    /// <summary>
    /// Resolves the caller, or returns null when the token names a user that no longer exists.
    /// </summary>
    public async Task<SubscriberIdentity?> ResolveAsync(
        ClaimsPrincipal principal,
        string? firstName = null,
        string? lastName = null)
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

        // eShopOnWeb registers users by email address, so UserName doubles as the contact address
        // when the Email column is not populated.
        var email = string.IsNullOrWhiteSpace(user.Email) ? user.UserName : user.Email;
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        return new SubscriberIdentity(user.UserName ?? userName, email, firstName, lastName);
    }
}
