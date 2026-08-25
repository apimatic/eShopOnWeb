using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Models.SubscriptionBilling;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the JWT-authenticated caller to the subscriber identity used for billing.
/// </summary>
internal static class SubscriberContext
{
    public static async Task<SubscriberInfo?> ResolveAsync(HttpContext? httpContext, UserManager<ApplicationUser> userManager)
    {
        var username = httpContext?.User.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(username))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(username);
        if (user is null)
        {
            return null;
        }

        return new SubscriberInfo(user.Id, user.Email ?? username);
    }
}
