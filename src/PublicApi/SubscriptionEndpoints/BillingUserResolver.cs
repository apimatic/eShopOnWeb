using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Billing;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class BillingUserResolver
{
    /// <summary>Resolves the caller from the JWT name claim; null when the token's user does not exist.</summary>
    public static async Task<BillingUser?> ResolveAsync(HttpContext httpContext)
    {
        var userName = httpContext.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var userManager = httpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByNameAsync(userName);
        if (user?.NormalizedUserName is null)
        {
            return null;
        }

        return new BillingUser(user.NormalizedUserName, user.Email ?? user.UserName ?? userName);
    }
}
