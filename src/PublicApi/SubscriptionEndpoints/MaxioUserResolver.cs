using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the authenticated eShopOnWeb user (from the JWT) into a <see cref="MaxioUserContext"/>.
/// The caller's identity comes from the token — the JWT <c>name</c> claim — never from the request body.
/// </summary>
public static class MaxioUserResolver
{
    public static async Task<MaxioUserContext?> ResolveAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var userName = principal.Identity?.Name ?? principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName))
            return null;

        var appUser = await userManager.FindByNameAsync(userName);
        if (appUser is null)
            return null;

        var email = string.IsNullOrWhiteSpace(appUser.Email) ? userName : appUser.Email!;
        var atIndex = email.IndexOf('@');
        var firstName = atIndex > 0 ? email.Substring(0, atIndex) : email;

        return new MaxioUserContext(
            EShopUserId: appUser.Id,
            Email: email,
            FirstName: firstName,
            LastName: "(eShopOnWeb)");
    }
}
