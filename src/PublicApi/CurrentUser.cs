using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi;

internal static class CurrentUser
{
    public static async Task<ApplicationUser?> ResolveAsync(HttpContext http, UserManager<ApplicationUser> userManager)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrEmpty(userId))
        {
            var byId = await userManager.FindByIdAsync(userId);
            if (byId is not null)
            {
                return byId;
            }
        }

        var name = http.User.Identity?.Name;
        if (!string.IsNullOrEmpty(name))
        {
            return await userManager.FindByNameAsync(name);
        }

        return null;
    }

    public static (string FirstName, string LastName) SplitName(ApplicationUser user)
    {
        var source = user.Email ?? user.UserName ?? "shopper";
        var local = source.Split('@')[0];
        if (string.IsNullOrWhiteSpace(local))
        {
            local = "shopper";
        }

        return (local, "eShopOnWeb");
    }
}
