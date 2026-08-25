using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionEndpointHelpers
{
    /// <summary>
    /// Resolves the caller from the JWT (username claim) to the Identity user,
    /// whose Id is used as the Maxio customer reference.
    /// </summary>
    public static async Task<ApplicationUser?> GetCurrentUserAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var userName = principal.Identity?.Name
                       ?? principal.FindFirst(ClaimTypes.Name)?.Value
                       ?? principal.FindFirst("unique_name")?.Value;
        if (string.IsNullOrEmpty(userName))
            return null;

        return await userManager.FindByNameAsync(userName);
    }

    public static IResult ToErrorResult(MaxioApiException exception)
    {
        var statusCode = exception.StatusCode is >= 400 and < 500
            ? exception.StatusCode
            : StatusCodes.Status502BadGateway;

        return Results.Problem(
            detail: $"Billing provider error ({exception.StatusCode}): {exception.ResponseBody}",
            statusCode: statusCode,
            title: "Subscription billing request failed");
    }
}
