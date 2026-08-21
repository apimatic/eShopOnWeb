using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class CurrentShopper
{
    public static async Task<(ShopperIdentity? Identity, IResult? Error)> ResolveAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> users)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return (null, Results.Unauthorized());
        }

        var userName = principal.Identity.Name;
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = principal.FindFirstValue(ClaimTypes.Email);

        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(email))
        {
            if (string.IsNullOrWhiteSpace(userName))
            {
                return (null, Results.Unauthorized());
            }

            var user = await users.FindByNameAsync(userName);
            if (user is null)
            {
                return (null, Results.Unauthorized());
            }

            userId = user.Id;
            email = user.Email ?? userName;
            userName = user.UserName ?? userName;
        }

        return (new ShopperIdentity(userId, email, userName), null);
    }
}
