using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Mindbody;
using Microsoft.eShopWeb.PublicApi.Studio;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

/// <summary>Maps the eShop identity (from the JWT, or named by an administrator) to a studio shopper.</summary>
public static class StudioShopperResolver
{
    public static async Task<StudioShopper> FromPrincipalAsync(UserManager<ApplicationUser> userManager, ClaimsPrincipal principal)
    {
        var name = principal.Identity?.Name;
        var user = string.IsNullOrEmpty(name) ? null : await userManager.FindByNameAsync(name);
        if (user is null)
            throw new StudioException(401, "The signed-in user is not known.");
        return ToShopper(user);
    }

    public static async Task<StudioShopper> FromNameAsync(UserManager<ApplicationUser> userManager, string? shopper)
    {
        if (string.IsNullOrWhiteSpace(shopper))
            throw new StudioException(400, "'shopper' is required (the shopper's e-mail address).");
        var user = await userManager.FindByEmailAsync(shopper.Trim()) ?? await userManager.FindByNameAsync(shopper.Trim());
        if (user is null)
            throw new StudioException(404, "There is no such shopper.");
        return ToShopper(user);
    }

    private static StudioShopper ToShopper(ApplicationUser user)
    {
        var email = !string.IsNullOrWhiteSpace(user.Email) ? user.Email
            : user.UserName is not null && user.UserName.Contains('@') ? user.UserName
            : null;
        if (email is null)
            throw new StudioException(400, "The account has no e-mail address, which the studio needs to identify a client.");
        return new StudioShopper(email.Trim(), user.UserName ?? email);
    }
}
