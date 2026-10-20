using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class BillingCaller
{
    /// <summary>
    /// The caller is always the token's user (ClaimTypes.Name); nothing identity-related is read from the request.
    /// Returns null when the token carries no usable name.
    /// </summary>
    public static async Task<BillingUser?> ResolveAsync(HttpContext http)
    {
        var name = http.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var users = http.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByNameAsync(name);
        return new BillingUser(name, user?.Email);
    }
}
