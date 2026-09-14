using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Turns the bearer token's identity into the <see cref="Subscriber"/> the billing system is
/// told about. The caller never supplies who they are: it comes from the token only.
/// </summary>
internal static class SubscriberResolver
{
    public static async Task<Subscriber?> ResolveAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var userName = principal.FindFirstValue(ClaimTypes.Name) ?? principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return null;
        }

        // The user name, not the Identity primary key, is the billing customer reference. It is
        // the identifier that stays put across restarts - including on the in-memory provider
        // this sample can run on, where Identity row ids are regenerated on every boot.
        var userKey = user.NormalizedUserName ?? user.UserName ?? userName;

        return new Subscriber
        {
            UserKey = userKey,
            Email = user.Email ?? user.UserName ?? userKey,
            Organization = "eShopOnWeb"
        };
    }
}
