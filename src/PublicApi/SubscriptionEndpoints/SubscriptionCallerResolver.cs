using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the billing identity of the caller from the JWT (never from the request body).
/// </summary>
public static class SubscriptionCallerResolver
{
    public static async Task<SubscriberIdentity?> ResolveAsync(HttpContext httpContext)
    {
        var userName = httpContext.User.Identity?.Name;
        if (string.IsNullOrEmpty(userName))
        {
            return null;
        }

        var userManager = httpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByNameAsync(userName);
        if (user == null)
        {
            return null;
        }

        return new SubscriberIdentity(user.Id, user.Email ?? user.UserName ?? userName, null, null);
    }
}
