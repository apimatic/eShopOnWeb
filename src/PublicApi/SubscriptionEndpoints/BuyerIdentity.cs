using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>The authenticated shopper, resolved from the JWT's name claim to the Identity user record.</summary>
public record BuyerIdentity(string BuyerId, string Email)
{
    public static async Task<BuyerIdentity?> ResolveAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var userName = principal.Identity?.IsAuthenticated == true ? principal.Identity.Name : null;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return null;
        }

        var email = string.IsNullOrWhiteSpace(user.Email) ? userName : user.Email;
        return new BuyerIdentity(user.Id, email);
    }
}
