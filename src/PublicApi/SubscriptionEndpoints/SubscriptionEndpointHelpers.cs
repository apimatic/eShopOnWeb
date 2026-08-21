using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionEndpointHelpers
{
    public static async Task<ApplicationUser?> GetUserAsync(HttpContext context, UserManager<ApplicationUser> userManager)
    {
        var userName = context.User.Identity?.Name;
        return string.IsNullOrWhiteSpace(userName) ? null : await userManager.FindByNameAsync(userName);
    }

    public static IResult MaxioUnavailable(MaxioApiException exception) => Results.Problem(
        title: "Subscription billing is unavailable",
        detail: exception.ResponseMessage,
        statusCode: StatusCodes.Status502BadGateway);
}
