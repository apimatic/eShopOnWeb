using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The authenticated shopper behind a request, bound with <c>[AsParameters]</c>. The identity comes from the
/// JWT only (its name claim); the account is looked up so the buyer id is the canonical user name.
/// </summary>
public class ShopperContext
{
    public ShopperContext(ClaimsPrincipal user, UserManager<ApplicationUser> userManager, CancellationToken cancellationToken)
    {
        User = user;
        UserManager = userManager;
        CancellationToken = cancellationToken;
    }

    public ClaimsPrincipal User { get; }
    public UserManager<ApplicationUser> UserManager { get; }
    public CancellationToken CancellationToken { get; }

    public async Task<ApplicationUser?> FindAccountAsync()
    {
        var userName = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var account = await UserManager.FindByNameAsync(userName);
        return string.IsNullOrWhiteSpace(account?.UserName) ? null : account;
    }
}
