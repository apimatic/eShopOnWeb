using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class SubscriberIdentityResolver
{
    public static async Task<SubscriberIdentity?> ResolveAsync(
        System.Security.Claims.ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user == null)
        {
            return null;
        }

        var email = string.IsNullOrWhiteSpace(user.Email) ? user.UserName! : user.Email;
        return new SubscriberIdentity(user.Id, email);
    }
}
