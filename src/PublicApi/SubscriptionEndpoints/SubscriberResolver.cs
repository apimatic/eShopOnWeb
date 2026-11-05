using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class SubscriberResolver
{
    /// <summary>
    /// Resolves the caller from the JWT identity (the token carries the user name). The stable Identity
    /// user id becomes the billing customer reference. Returns null if the token's user no longer exists.
    /// </summary>
    public static async Task<Subscriber?> ResolveAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrEmpty(userName)) return null;

        var user = await userManager.FindByNameAsync(userName);
        if (user is null) return null;

        return new Subscriber(user.Id, user.Email ?? user.UserName ?? userName);
    }
}
