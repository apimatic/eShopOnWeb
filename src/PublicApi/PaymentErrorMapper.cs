using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Maps payment-domain exceptions to HTTP results, and extracts the caller's
/// identity (buyer id = username) from the JWT.
/// </summary>
public static class PaymentEndpointHelpers
{
    public static string? GetBuyerId(ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.Name)?.Value
            ?? user.Identity?.Name
            ?? user.FindFirst("name")?.Value;
    }

    public static IResult? TryMapException(Exception ex)
    {
        return ex switch
        {
            PaymentResourceNotFoundException notFound =>
                Results.NotFound(new { message = notFound.Message }),

            InvalidPaymentStateException invalidState =>
                Results.Conflict(new { message = invalidState.Message }),

            RefundExceedsCapturedException refundExceeds =>
                Results.UnprocessableEntity(new { message = refundExceeds.Message }),

            AuthorizationNotRenewableException notRenewable =>
                Results.Conflict(new { message = notRenewable.Message }),

            PaymentDeclinedException declined =>
                Results.UnprocessableEntity(new { message = declined.Message }),

            PayerActionRequiredException payerAction =>
                Results.UnprocessableEntity(new { message = payerAction.Message }),

            PaymentGatewayException gateway when gateway.ErrorName == "DUPLICATE_REQUEST_ID" =>
                Results.Conflict(new
                {
                    message = "This idempotency key was already used for a different request. Supply a fresh key for a new refund.",
                    payPalError = gateway.ErrorName,
                    payPalDebugId = gateway.DebugId
                }),

            PaymentGatewayException gateway =>
                Results.Json(new
                {
                    message = gateway.Message,
                    payPalError = gateway.ErrorName,
                    payPalDebugId = gateway.DebugId
                }, statusCode: gateway.HttpStatusCode == 422 ? 422 : 502),

            ArgumentException argument =>
                Results.BadRequest(new { message = argument.Message }),

            _ => null
        };
    }
}
