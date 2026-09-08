using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps billing failures onto HTTP outcomes without leaking provider internals.
/// </summary>
public static class SubscriptionErrorMapper
{
    public static ActionResult ToActionResult(this MaxioBillingException ex)
    {
        var statusCode = ex.Kind switch
        {
            MaxioBillingErrorKind.Rejected => ex.ProviderStatusCode switch
            {
                404 => StatusCodes.Status404NotFound,
                422 => StatusCodes.Status422UnprocessableEntity,
                >= 400 and < 500 => StatusCodes.Status400BadRequest,
                _ => StatusCodes.Status502BadGateway
            },
            MaxioBillingErrorKind.Unreachable => StatusCodes.Status503ServiceUnavailable,
            MaxioBillingErrorKind.Unparseable => StatusCodes.Status502BadGateway,
            _ => StatusCodes.Status502BadGateway
        };

        return new ObjectResult(new ProblemDetails
        {
            Status = statusCode,
            Title = "Subscription billing error",
            Detail = ex.Message
        })
        {
            StatusCode = statusCode
        };
    }
}
