using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Stax;

public static class Shopper
{
    /// <summary>The caller's identity comes from the JWT; a closed (deleted) account can no longer act.</summary>
    public static async Task<ApplicationUser> RequireAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> users)
    {
        var name = principal.Identity?.Name ?? principal.FindFirstValue(ClaimTypes.Name);
        var user = string.IsNullOrEmpty(name) ? null : await users.FindByNameAsync(name);
        return user ?? throw new ApiException(401, "The account does not exist or has been closed.");
    }

    public static string EmailOf(ApplicationUser user) => user.Email ?? user.UserName!;
}
