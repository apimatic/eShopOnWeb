using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Mindbody;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

internal static class StudioIdentity
{
    /// <summary>Resolves the signed-in shopper's e-mail address from the identity in the JWT.</summary>
    public static async Task<string> GetShopperEmailAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var userName = principal.FindFirstValue(ClaimTypes.Name) ?? principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
            throw new StudioException(HttpStatusCode.Unauthorized, "The token does not identify a user.");
        var user = await userManager.FindByNameAsync(userName)
                   ?? throw new StudioException(HttpStatusCode.Unauthorized, "The signed-in user no longer exists.");
        return EmailOf(user);
    }

    public static string EmailOf(ApplicationUser user) => (user.Email ?? user.UserName)!.Trim();
}
