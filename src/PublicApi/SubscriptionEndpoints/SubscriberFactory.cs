using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the calling shopper from the bearer token. Callers never supply a customer id or
/// e-mail address of their own: identity comes from the JWT only.
/// </summary>
public interface ISubscriberFactory
{
    Task<Subscriber?> CreateAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default);
}

public class SubscriberFactory : ISubscriberFactory
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<SubscriberFactory> _logger;

    public SubscriberFactory(UserManager<ApplicationUser> userManager, ILogger<SubscriberFactory> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<Subscriber?> CreateAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        var userName = principal.Identity?.Name ?? principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user is not null)
        {
            var email = string.IsNullOrWhiteSpace(user.Email) ? user.UserName : user.Email;
            return string.IsNullOrWhiteSpace(email) ? null : new Subscriber(user.Id, email!);
        }

        // The token is signed by this application, so its subject is trustworthy even when the
        // identity store no longer holds the row (eShopOnWeb can run on an in-memory database that
        // is rebuilt on every start). eShopOnWeb user names are e-mail addresses.
        if (!userName.Contains('@'))
        {
            _logger.LogWarning("Bearer token subject {UserName} is not a known user and is not an e-mail address", userName);
            return null;
        }

        _logger.LogInformation("Bearer token subject {UserName} was not found in the identity store; using token claims", userName);
        return new Subscriber(userName, userName);
    }
}
