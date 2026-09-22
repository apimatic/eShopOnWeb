using System;
using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Shared helpers for the payment endpoints: caller identity and failure-to-HTTP mapping.</summary>
internal static class PaymentEndpointHelpers
{
    /// <summary>The caller's identity from the JWT (the <c>name</c> claim). Endpoints are [Authorize]d, so it is present.</summary>
    public static string GetBuyerId(HttpContext http)
    {
        var buyerId = http.User.Identity?.Name;
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return buyerId!;
    }

    /// <summary>Maps a payment failure to an appropriate HTTP result with a caller-safe body.</summary>
    public static IResult MapError(Exception ex) => ex switch
    {
        PaymentOperationException op => op.Kind switch
        {
            PaymentOperationErrorKind.NotFound => Problem(StatusCodes.Status404NotFound, op.Message),
            PaymentOperationErrorKind.Conflict => Problem(StatusCodes.Status409Conflict, op.Message),
            _ => Problem(StatusCodes.Status400BadRequest, op.Message),
        },
        PaymentGatewayException gw => gw.Kind switch
        {
            // A browser 3DS/payer-action challenge is explicitly out of scope — report it, do not build it.
            PaymentGatewayFailureKind.ApprovalRequired =>
                Problem(StatusCodes.Status422UnprocessableEntity, gw.Message, gw.ErrorName, gw.DebugId),
            // A stale hold that cannot be renewed — the operator needs to act (re-collect payment).
            PaymentGatewayFailureKind.AuthorizationUnrenewable =>
                Problem(StatusCodes.Status409Conflict, gw.Message, gw.ErrorName, gw.DebugId),
            PaymentGatewayFailureKind.CallerError =>
                Problem(gw.StatusCode is >= 400 and < 500 ? gw.StatusCode.Value : StatusCodes.Status422UnprocessableEntity,
                    gw.Message, gw.ErrorName, gw.DebugId),
            // Our credentials/quota or PayPal itself — the caller cannot fix it.
            _ => Problem(StatusCodes.Status502BadGateway, gw.Message, gw.ErrorName, gw.DebugId),
        },
        _ => Problem(StatusCodes.Status500InternalServerError, "An unexpected error occurred."),
    };

    private static IResult Problem(int status, string message, string? errorName = null, string? debugId = null) =>
        Results.Json(new PaymentErrorResponse
        {
            Status = status,
            Message = message,
            ErrorName = errorName,
            DebugId = debugId,
        }, statusCode: status);
}

/// <summary>A caller-safe error body. Carries PayPal's debug id when present so an operator can investigate.</summary>
public class PaymentErrorResponse
{
    public int Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ErrorName { get; set; }
    public string? DebugId { get; set; }
}
