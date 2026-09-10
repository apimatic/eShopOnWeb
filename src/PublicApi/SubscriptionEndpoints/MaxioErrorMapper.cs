using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Translates <see cref="MaxioBillingException"/> into an appropriate HTTP response so that
/// billing validation problems surface as 4xx and upstream/config failures as 5xx, each with
/// the underlying Maxio messages included for the caller.
/// </summary>
internal static class MaxioErrorMapper
{
    public static IResult ToResult(MaxioBillingException exception)
    {
        // Missing/invalid configuration – the integration cannot serve the request.
        if (exception is MaxioConfigurationException)
        {
            return Results.Json(
                new { message = exception.Message, errors = exception.Errors },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var statusCode = exception.StatusCode switch
        {
            // Maxio validation errors (e.g. unknown plan handle) -> 400 Bad Request.
            400 or 422 => StatusCodes.Status400BadRequest,
            404 => StatusCodes.Status404NotFound,
            409 => StatusCodes.Status409Conflict,
            429 => StatusCodes.Status429TooManyRequests,
            // Any other failure is an upstream billing problem.
            _ => StatusCodes.Status502BadGateway
        };

        return Results.Json(
            new { message = exception.Message, errors = exception.Errors },
            statusCode: statusCode);
    }
}
