using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi;

internal static class BillingCustomerResolver
{
    public static async Task<BillingCustomer?> ResolveAsync(UserManager<ApplicationUser> userManager, ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var user = await userManager.GetUserAsync(principal);
        if (user is null && !string.IsNullOrWhiteSpace(principal.Identity.Name))
        {
            user = await userManager.FindByNameAsync(principal.Identity.Name);
        }

        if (user is null || string.IsNullOrWhiteSpace(user.Id))
        {
            return null;
        }

        return BillingCustomer.FromUser(user.Id, user.Email, user.UserName);
    }
}
