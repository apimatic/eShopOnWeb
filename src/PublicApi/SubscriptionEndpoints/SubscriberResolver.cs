using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the eShopOnWeb user behind the JWT. The token carries the user name as it was typed at sign-in, so the
/// identity id, normalized user name and e-mail come from the Identity store.
/// </summary>
public static class SubscriberResolver
{
    public static async Task<Subscriber?> ResolveAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(userName);
        var email = user?.Email ?? user?.UserName;
        if (user is null || string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        return new Subscriber(user.Id, user.NormalizedUserName ?? userManager.NormalizeName(userName), email);
    }
}
