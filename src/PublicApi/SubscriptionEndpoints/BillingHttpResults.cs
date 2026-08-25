using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class BillingHttpResults
{
    public static IResult FromException(BillingException exception)
    {
        var statusCode = exception.Kind switch
        {
            BillingFailureKind.InvalidRequest => StatusCodes.Status400BadRequest,
            BillingFailureKind.NotFound => StatusCodes.Status404NotFound,
            BillingFailureKind.Conflict => StatusCodes.Status409Conflict,
            BillingFailureKind.ProviderUnavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status502BadGateway
        };

        return Results.Problem(
            title: "Subscription billing request failed",
            detail: exception.Message,
            statusCode: statusCode);
    }
}
