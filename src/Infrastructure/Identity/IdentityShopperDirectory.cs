using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class IdentityShopperDirectory : IShopperDirectory
{
    private readonly UserManager<ApplicationUser> _userManager;

    public IdentityShopperDirectory(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<Shopper?> FindByUserNameAsync(string userName, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByNameAsync(userName);
        if (user?.UserName is null)
        {
            return null;
        }

        return new Shopper(user.Id, user.UserName, user.Email ?? user.UserName);
    }
}
