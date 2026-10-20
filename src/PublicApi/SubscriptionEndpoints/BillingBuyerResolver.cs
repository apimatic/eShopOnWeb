using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the billing buyer from the caller's token. Identity comes only from the token — never from the
/// request body — and must still name an existing user.
/// </summary>
public static class BillingBuyerResolver
{
    public static async Task<BillingBuyer?> ResolveAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return null;
        }

        var resolvedName = user.UserName ?? userName;
        return new BillingBuyer(resolvedName, string.IsNullOrWhiteSpace(user.Email) ? resolvedName : user.Email);
    }
}
