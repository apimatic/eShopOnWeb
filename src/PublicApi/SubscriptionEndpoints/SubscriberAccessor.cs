using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the billing <see cref="Subscriber"/> for the caller identified by the bearer token.
/// </summary>
public interface ISubscriberAccessor
{
    /// <summary>
    /// Projects the authenticated principal onto a <see cref="Subscriber"/>, or returns <c>null</c> when the
    /// token does not identify a user.
    /// </summary>
    Task<Subscriber?> GetSubscriberAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public class SubscriberAccessor : ISubscriberAccessor
{
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriberAccessor(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<Subscriber?> GetSubscriberAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        // The token issued by api/authenticate carries the user name as its name claim.
        var userName = principal.Identity?.Name ?? principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        // The e-mail address on the identity record is preferred for the billing customer; fall back to the
        // user name, which is itself an e-mail address in eShopOnWeb.
        var user = await _userManager.FindByNameAsync(userName);

        return Subscriber.FromIdentity(userName, user?.Email);
    }
}
