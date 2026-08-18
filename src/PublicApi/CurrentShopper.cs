using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi;

internal static class CurrentShopper
{
    public static async Task<(ApplicationUser? User, IResult? Error)> GetAsync(
        UserManager<ApplicationUser> userManager,
        ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(principal.Identity.Name))
        {
            return (null, Results.Unauthorized());
        }

        var user = await userManager.FindByNameAsync(principal.Identity.Name);
        if (user is null)
        {
            return (null, Results.Unauthorized());
        }

        return (user, null);
    }
}
