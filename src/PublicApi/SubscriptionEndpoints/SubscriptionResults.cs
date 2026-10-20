using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Shared by the subscription endpoints: caller identity and the one failure mapping.</summary>
internal static class SubscriptionHttp
{
    /// <summary>The token carries only the user name; the stable user id is resolved server-side, never supplied by the caller.</summary>
    public static async Task<ApplicationUser?> ResolveUserAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> users)
    {
        var name = principal.Identity?.Name;
        return string.IsNullOrEmpty(name) ? null : await users.FindByNameAsync(name);
    }

    public static IResult ToResult(BillingException ex) => ex.Failure switch
    {
        BillingFailure.InvalidPlan => Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest),
        BillingFailure.NotFound => Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound),
        BillingFailure.InProgress => Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict),
        BillingFailure.Rejected => Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity),
        _ => Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway)
    };
}
