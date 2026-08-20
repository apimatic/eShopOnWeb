using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionEndpointResults
{
    public static string? UserId(ClaimsPrincipal principal) =>
        principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    public static IResult From(BillingProviderException exception)
    {
        var status = exception.Kind switch
        {
            BillingFailureKind.InvalidRequest => exception.ProviderStatusCode == 401 ? StatusCodes.Status401Unauthorized : StatusCodes.Status422UnprocessableEntity,
            BillingFailureKind.NotFound => StatusCodes.Status404NotFound,
            BillingFailureKind.Indeterminate => StatusCodes.Status503ServiceUnavailable,
            BillingFailureKind.Misconfigured => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status502BadGateway
        };

        return Results.Problem(statusCode: status, title: exception.Message);
    }
}
