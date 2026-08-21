using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

internal sealed record ShopperIdentity(string Id, string Email);

internal interface IShopperIdentityService
{
    Task<ShopperIdentity?> FindByNameAsync(string userName);
}

internal sealed class ShopperIdentityService : IShopperIdentityService
{
    private readonly UserManager<ApplicationUser> _userManager;

    public ShopperIdentityService(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<ShopperIdentity?> FindByNameAsync(string userName)
    {
        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return null;
        }

        var email = user.Email ?? user.UserName;
        return string.IsNullOrWhiteSpace(email) ? null : new ShopperIdentity(user.Id, email);
    }
}
