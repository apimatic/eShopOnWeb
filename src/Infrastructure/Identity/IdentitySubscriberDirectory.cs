using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class IdentitySubscriberDirectory : ISubscriberDirectory
{
    private readonly UserManager<ApplicationUser> _userManager;

    public IdentitySubscriberDirectory(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<Subscriber?> FindByUserNameAsync(string userName)
    {
        var user = await _userManager.FindByNameAsync(userName);
        if (user?.UserName is null)
        {
            return null;
        }
        return new Subscriber(user.UserName, user.Email ?? user.UserName);
    }
}
