using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi;

internal static class ShopperIdentityResolver
{
    public static async Task<ShopperIdentity?> ResolveAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
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

        if (string.IsNullOrWhiteSpace(user.Email))
        {
            throw new CustomerEmailRequiredException();
        }

        var localPart = user.Email.Split('@')[0];
        var firstName = string.IsNullOrWhiteSpace(localPart) ? "Customer" : localPart;
        return new ShopperIdentity(user.Id, user.Email, firstName, "Customer");
    }
}
