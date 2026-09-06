using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionEndpointHelpers
{
    public static async Task<Shopper?> GetShopperAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var username = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
            return null;

        var user = await userManager.FindByNameAsync(username);
        if (user?.Email is null)
            return null;

        return new Shopper(user.Id, user.Email);
    }

    public static IResult MaxioFailure(MaxioApiException exception)
    {
        var statusCode = exception.StatusCode == System.Net.HttpStatusCode.TooManyRequests
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status502BadGateway;
        return Results.Problem("The subscription service is temporarily unavailable.", statusCode: statusCode);
    }
}
