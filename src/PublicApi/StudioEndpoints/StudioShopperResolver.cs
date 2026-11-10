using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Mindbody;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

/// <summary>Turns eShop identities (the JWT caller, or a shopper named by staff) into studio shoppers.</summary>
public static class StudioShopperResolver
{
    public static async Task<StudioShopper> FromPrincipalAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> users)
    {
        var name = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name)) throw new StudioException(401, "The token does not identify a shopper.");
        return await FromUserNameAsync(name, users, 403);
    }

    public static async Task<StudioShopper> FromUserNameAsync(string identity, UserManager<ApplicationUser> users, int notFoundStatus = 404)
    {
        var user = await users.FindByNameAsync(identity) ?? await users.FindByEmailAsync(identity);
        if (user is null) throw new StudioException(notFoundStatus, "No eShop account was found for " + identity + ".");
        var email = !string.IsNullOrWhiteSpace(user.Email) ? user.Email
            : (user.UserName is not null && user.UserName.Contains('@') ? user.UserName : null);
        if (email is null) throw StudioException.Unprocessable("The eShop account has no email address to register with the studio.");
        return new StudioShopper(email);
    }
}
