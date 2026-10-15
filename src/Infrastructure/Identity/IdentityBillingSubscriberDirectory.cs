using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class IdentityBillingSubscriberDirectory : IBillingSubscriberDirectory
{
    private readonly UserManager<ApplicationUser> _userManager;

    public IdentityBillingSubscriberDirectory(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<BillingSubscriber?> FindAsync(string userName, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return null;
        }

        var email = string.IsNullOrWhiteSpace(user.Email) ? user.UserName : user.Email;
        return string.IsNullOrWhiteSpace(email) ? null : new BillingSubscriber(user.UserName ?? userName, email);
    }
}
