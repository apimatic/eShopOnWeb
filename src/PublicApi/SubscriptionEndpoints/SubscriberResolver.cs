using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the billed user from the JWT. The token's name claim is the only identity used;
/// nothing in the request body can choose whose account is billed.
/// </summary>
public static class SubscriberResolver
{
    public static async Task<Subscriber?> ResolveAsync(HttpContext httpContext)
    {
        var userName = httpContext.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var userManager = httpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return null;
        }

        return new Subscriber(user.UserName ?? userName, user.Email ?? userName);
    }
}
