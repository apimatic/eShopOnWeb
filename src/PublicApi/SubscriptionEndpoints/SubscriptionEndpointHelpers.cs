using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the authenticated caller (from the JWT identity) into a <see cref="BillingUser"/> using the
/// stable eShopOnWeb user id as the Maxio customer reference.
/// </summary>
public static class BillingUserResolver
{
    public static async Task<BillingUser?> ResolveAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var userName = principal.Identity?.Name ?? principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user == null)
        {
            return null;
        }

        // The eShop user id (a stable GUID) is used as the Maxio customer reference so the mapping survives
        // even if the username/email changes.
        return new BillingUser(user.Id, user.Email ?? userName);
    }
}

/// <summary>Translates a <see cref="MaxioApiException"/> into an appropriate HTTP result for API callers.</summary>
public static class MaxioProblem
{
    public static IResult ToResult(MaxioApiException ex)
    {
        var status = ex.StatusCode switch
        {
            HttpStatusCode.UnprocessableEntity => StatusCodes.Status422UnprocessableEntity,
            HttpStatusCode.NotFound => StatusCodes.Status404NotFound,
            HttpStatusCode.BadRequest => StatusCodes.Status400BadRequest,
            HttpStatusCode.TooManyRequests => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status502BadGateway
        };

        return Results.Problem(
            detail: ex.ResponseBody ?? ex.Message,
            statusCode: status,
            title: "Maxio billing request failed");
    }
}
