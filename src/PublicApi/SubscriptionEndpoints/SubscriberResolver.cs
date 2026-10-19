using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class SubscriberResolver
{
    /// <summary>
    /// The billing subscriber for the JWT caller: the token's name claim identifies the user and the identity
    /// store supplies the e-mail. Null when the token names no (longer existing) user.
    /// </summary>
    public static async Task<Subscriber?> ResolveAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
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

        return new Subscriber(user.UserName ?? userName, user.Email ?? userName);
    }
}
