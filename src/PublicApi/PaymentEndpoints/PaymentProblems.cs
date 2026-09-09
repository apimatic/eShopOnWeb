using System;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Maps domain and PayPal exceptions to appropriate HTTP problem responses.</summary>
public static class PaymentProblems
{
    public static IResult ToResult(Exception ex) => ex switch
    {
        OrderNotFoundException => Results.NotFound(new { message = ex.Message }),
        PaymentMethodNotFoundException => Results.NotFound(new { message = ex.Message }),
        InvalidPaymentSourceException => Results.BadRequest(new { message = ex.Message }),

        // The card payment needs a browser approval (e.g. 3-D Secure), which this integration does not do.
        PayPalChallengeRequiredException => Results.Json(
            new { message = ex.Message }, statusCode: StatusCodes.Status409Conflict),

        // The hold can no longer be renewed — phrased for an operator to act on.
        AuthorizationNotRenewableException => Results.Json(
            new { message = ex.Message }, statusCode: StatusCodes.Status409Conflict),

        // A PayPal-reported failure (e.g. a declined card). Surface its message, status and debug id.
        PayPalApiException pex => Results.Json(
            new { message = pex.Message, issue = pex.Issue, debugId = pex.DebugId },
            statusCode: pex.StatusCode is >= 400 and < 500 ? StatusCodes.Status402PaymentRequired : StatusCodes.Status502BadGateway),

        // An illegal lifecycle transition (e.g. fulfilling an unpaid order).
        InvalidOperationException => Results.Json(
            new { message = ex.Message }, statusCode: StatusCodes.Status409Conflict),

        _ => Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status500InternalServerError)
    };
}
