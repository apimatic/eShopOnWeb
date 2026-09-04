using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

internal static class SubscriptionEndpointHelpers
{
    public static async Task<ApplicationUser?> GetCurrentUserAsync(
        HttpContext context, UserManager<ApplicationUser> userManager)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrWhiteSpace(userId))
        {
            return await userManager.FindByIdAsync(userId);
        }

        var userName = context.User.FindFirstValue(ClaimTypes.Name);
        return string.IsNullOrWhiteSpace(userName)
            ? null
            : await userManager.FindByNameAsync(userName);
    }

    public static IResult ServiceUnavailable() =>
        Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Subscription billing is temporarily unavailable.");

    public static IResult MaxioFailure() =>
        Results.Problem(statusCode: StatusCodes.Status502BadGateway,
            title: "Subscription billing could not be completed.");
}
