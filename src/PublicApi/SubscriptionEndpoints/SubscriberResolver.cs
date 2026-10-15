using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Resolves the calling shopper from the JWT (its name claim) against the identity store.</summary>
public static class SubscriberResolver
{
    public static async Task<Subscriber?> ResolveAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager,
        string? firstName = null, string? lastName = null)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user?.UserName is null)
        {
            return null;
        }

        return new Subscriber(user.UserName, user.Email ?? user.UserName, firstName, lastName);
    }
}
