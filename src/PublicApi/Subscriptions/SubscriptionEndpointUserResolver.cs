using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionEndpointUserResolver
{
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriptionEndpointUserResolver(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public Task<ApplicationUser?> ResolveAsync(ClaimsPrincipal principal)
    {
        var username = principal.Identity?.Name;
        return string.IsNullOrWhiteSpace(username)
            ? Task.FromResult<ApplicationUser?>(null)
            : _userManager.FindByNameAsync(username);
    }
}
