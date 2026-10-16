using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class ShopperResolver
{
    public static async Task<(ShopperAccount? Account, IResult? Error)> ResolveAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> users)
    {
        var name = principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(name))
            return (null, Results.Json(new { message = "Authentication is required." }, statusCode: StatusCodes.Status401Unauthorized));

        var user = await users.FindByNameAsync(name);
        if (user is null || string.IsNullOrWhiteSpace(user.Id))
            return (null, Results.Json(new { message = "The shopper was not found." }, statusCode: StatusCodes.Status404NotFound));

        return (new ShopperAccount { UserId = user.Id, Email = user.Email ?? "" }, null);
    }
}
