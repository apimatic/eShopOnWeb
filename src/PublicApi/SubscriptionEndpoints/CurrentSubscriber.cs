using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Builds the subscriber identity from the validated bearer token. The caller never states who they
/// are: the name claim is the only input, and the shopper's email is read from the identity store
/// rather than trusted from the request body.
/// </summary>
public class CurrentSubscriber : ICurrentSubscriber
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<ApplicationUser> _userManager;

    public CurrentSubscriber(IHttpContextAccessor httpContextAccessor, UserManager<ApplicationUser> userManager)
    {
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;
    }

    public async Task<SubscriberIdentity?> GetAsync()
    {
        var userName = _httpContextAccessor.HttpContext?.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            // The token is well-formed but the account behind it is gone.
            return null;
        }

        return new SubscriberIdentity(
            userName: user.UserName ?? userName,
            email: string.IsNullOrWhiteSpace(user.Email) ? userName : user.Email!);
    }
}
