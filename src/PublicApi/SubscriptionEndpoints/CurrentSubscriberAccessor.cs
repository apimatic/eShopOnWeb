using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the billing <see cref="SubscriberInfo"/> for the caller of the current request. The identity is
/// taken solely from the authenticated principal (the JWT); the stable user name becomes the billing
/// customer's reference so the same user always maps to the same billing customer.
/// </summary>
public interface ICurrentSubscriberAccessor
{
    Task<SubscriberInfo?> GetCurrentAsync();
}

internal sealed class HttpContextSubscriberAccessor : ICurrentSubscriberAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<ApplicationUser> _userManager;

    public HttpContextSubscriberAccessor(IHttpContextAccessor httpContextAccessor, UserManager<ApplicationUser> userManager)
    {
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;
    }

    public async Task<SubscriberInfo?> GetCurrentAsync()
    {
        var userName = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return null;
        }

        var email = string.IsNullOrWhiteSpace(user.Email) ? user.UserName! : user.Email!;
        return new SubscriberInfo(user.UserName!, email, FirstName: null, LastName: null);
    }
}
