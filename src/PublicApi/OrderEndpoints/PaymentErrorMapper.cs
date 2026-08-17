using System;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Maps application/PayPal exceptions to HTTP results with an actionable message. Card data is
/// never part of any exception, so nothing sensitive is surfaced.
/// </summary>
public static class PaymentErrorMapper
{
    public static bool TryMap(Exception ex, out IResult result)
    {
        switch (ex)
        {
            case PaymentValidationException:
                result = Problem(StatusCodes.Status400BadRequest, ex.Message);
                return true;
            case PaymentNotFoundException:
                result = Problem(StatusCodes.Status404NotFound, ex.Message);
                return true;
            case PaymentConflictException:
                result = Problem(StatusCodes.Status409Conflict, ex.Message);
                return true;
            case AuthorizationCannotBeRenewedException:
                // Phrased for an operator to act on (ask the shopper to pay again).
                result = Problem(StatusCodes.Status409Conflict, ex.Message);
                return true;
            case PayPalInstrumentDeclinedException:
                result = Problem(StatusCodes.Status422UnprocessableEntity, ex.Message);
                return true;
            case PayPalChallengeRequiredException:
                result = Problem(StatusCodes.Status422UnprocessableEntity, ex.Message);
                return true;
            case PayPalApiException papi:
                result = Problem(StatusCodes.Status502BadGateway,
                    $"The payment provider could not complete the request: {papi.Message}");
                return true;
            default:
                result = Results.Empty;
                return false;
        }
    }

    private static IResult Problem(int statusCode, string message) =>
        Results.Json(new { statusCode, message }, statusCode: statusCode);
}
