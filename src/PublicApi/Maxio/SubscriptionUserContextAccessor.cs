using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Resolves the JWT-authenticated caller to a billing customer context. The JWT only
/// carries the username claim, so the stable identity user id and email come from the
/// identity store.
/// </summary>
public class SubscriptionUserContextAccessor : ISubscriptionUserContextAccessor
{
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriptionUserContextAccessor(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<BillingCustomerContext> GetCurrentCustomerAsync(ClaimsPrincipal principal)
    {
        var username = principal.FindFirstValue(ClaimTypes.Name);
        var user = username is null ? null : await _userManager.FindByNameAsync(username);
        if (user is null)
        {
            throw new MaxioBillingException(HttpStatusCode.Unauthorized, "The current user could not be resolved.");
        }

        var email = user.Email ?? user.UserName ?? username!;
        var localPart = email.Split('@')[0];
        var firstName = string.IsNullOrWhiteSpace(localPart) ? "Customer" : localPart;

        return new BillingCustomerContext(user.Id, email, firstName, "Customer");
    }
}
