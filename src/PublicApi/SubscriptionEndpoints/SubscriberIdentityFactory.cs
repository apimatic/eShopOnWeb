using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriberIdentityFactory
{
    public static async Task<SubscriberIdentity?> FromHttpContextAsync(
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager)
    {
        var userName = httpContext.User.Identity?.Name
                       ?? httpContext.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user == null)
        {
            return null;
        }

        return new SubscriberIdentity(
            user.Id,
            user.Email ?? userName,
            user.UserName ?? userName);
    }
}
