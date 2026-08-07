using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Shared helpers so every payment endpoint identifies the caller and maps failures the same way.
/// </summary>
public static class PaymentEndpointExtensions
{
    /// <summary>The caller's identity (their username/email) from the JWT's name claim.</summary>
    public static bool TryGetBuyerId(this ClaimsPrincipal user, out string buyerId)
    {
        buyerId = user.Identity?.Name ?? string.Empty;
        return !string.IsNullOrEmpty(buyerId);
    }

    /// <summary>True for the domain/payment exceptions the endpoints translate into HTTP results.</summary>
    public static bool IsHandledPaymentException(this Exception exception) =>
        exception is OrderNotFoundException
            or SavedCardNotFoundException
            or PaymentStateException
            or PaymentException;

    /// <summary>
    /// Maps a handled exception to an HTTP result. The message is always caller-safe. A provider
    /// rejection surfaces as a client-actionable status; a provider fault as 5xx.
    /// </summary>
    public static IResult ToProblemResult(this Exception exception) => exception switch
    {
        OrderNotFoundException => Results.NotFound(new { message = exception.Message }),
        SavedCardNotFoundException => Results.NotFound(new { message = exception.Message }),
        PaymentStateException => Results.Conflict(new { message = exception.Message }),
        PaymentException { Kind: PaymentFailureKind.Rejected } =>
            Results.Json(new { message = exception.Message }, statusCode: StatusCodes.Status402PaymentRequired),
        PaymentException =>
            Results.Json(new { message = exception.Message }, statusCode: StatusCodes.Status502BadGateway),
        _ => Results.Json(new { message = "The request could not be processed." }, statusCode: StatusCodes.Status500InternalServerError)
    };
}
