using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class AuthenticatedShopperResolver
{
    private readonly UserManager<ApplicationUser> _userManager;

    public AuthenticatedShopperResolver(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<ShopperIdentity?> ResolveAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null || string.IsNullOrWhiteSpace(user.Email))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new ShopperIdentity(user.Id, user.UserName ?? userName, user.Email);
    }
}
