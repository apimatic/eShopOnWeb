using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface IShopperIdentityResolver
{
    Task<ShopperIdentity?> ResolveAsync(ClaimsPrincipal principal);
}

public sealed class ShopperIdentityResolver : IShopperIdentityResolver
{
    private readonly UserManager<ApplicationUser> _userManager;

    public ShopperIdentityResolver(UserManager<ApplicationUser> userManager) => _userManager = userManager;

    public async Task<ShopperIdentity?> ResolveAsync(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        ApplicationUser? user = null;
        if (!string.IsNullOrWhiteSpace(userId))
        {
            user = await _userManager.FindByIdAsync(userId);
        }

        // Supports JWTs issued by older eShopOnWeb builds while they remain valid.
        if (user is null && !string.IsNullOrWhiteSpace(principal.Identity?.Name))
        {
            user = await _userManager.FindByNameAsync(principal.Identity.Name);
        }

        var email = user?.Email ?? user?.UserName;
        return user is null || string.IsNullOrWhiteSpace(email)
            ? null
            : new ShopperIdentity(user.Id, email);
    }
}
