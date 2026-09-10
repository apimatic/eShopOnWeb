using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the authenticated caller (from the JWT) into a <see cref="SubscriberIdentity"/> for the
/// billing layer. The shopper's stable username is used as the Maxio customer reference, which keeps
/// the "ensure a customer exists" step idempotent across app restarts (the in-memory identity store
/// on this machine regenerates surrogate ids on every launch, but usernames are seeded stably).
/// </summary>
public sealed class SubscriberIdentityResolver
{
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriberIdentityResolver(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    /// <summary>
    /// Builds a <see cref="SubscriberIdentity"/> from the caller's principal, or returns
    /// <c>null</c> when the principal cannot be resolved to a known user.
    /// </summary>
    public async Task<SubscriberIdentity?> ResolveAsync(ClaimsPrincipal principal)
    {
        var userName = principal.Identity?.Name
                       ?? principal.FindFirstValue(ClaimTypes.Name);

        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await _userManager.FindByNameAsync(userName);

        var reference = user?.UserName ?? userName;
        var email = user?.Email ?? userName;

        var (firstName, lastName) = SplitName(email);
        return new SubscriberIdentity(reference, email, firstName, lastName);
    }

    private static (string FirstName, string LastName) SplitName(string email)
    {
        var atIndex = email.IndexOf('@');
        var localPart = atIndex > 0 ? email[..atIndex] : email;
        var firstName = string.IsNullOrWhiteSpace(localPart) ? "eShopOnWeb" : localPart;
        return (firstName, "eShopOnWeb");
    }
}
