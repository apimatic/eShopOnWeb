using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class BillingShopperResolver
{
    private readonly UserManager<ApplicationUser> _userManager;

    public BillingShopperResolver(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<BillingShopper> ResolveAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new BillingValidationException("The access token does not identify a user.");
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null || string.IsNullOrWhiteSpace(user.Id))
        {
            throw new BillingValidationException("The authenticated user could not be found.");
        }

        var email = user.Email ?? user.UserName ?? userName;
        return new BillingShopper(user.Id, email, user.UserName);
    }
}
