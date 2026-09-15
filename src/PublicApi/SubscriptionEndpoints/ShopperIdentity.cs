using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class ShopperIdentity
{
    public static async Task<ApplicationUser?> GetRequiredUserAsync(
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager)
    {
        var userName = httpContext.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        return await userManager.FindByNameAsync(userName);
    }

    public static string Email(ApplicationUser user)
        => user.Email ?? user.UserName ?? $"{user.Id}@eshoponweb.local";

    public static string FirstName(ApplicationUser user)
    {
        var email = Email(user);
        var local = email.Contains('@') ? email.Split('@')[0] : email;
        return string.IsNullOrWhiteSpace(local) ? "Shopper" : local;
    }

    public static string LastName(ApplicationUser user) => "Shopper";
}
