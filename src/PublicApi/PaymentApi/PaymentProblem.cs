using System;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.PaymentApi;

/// <summary>
/// Maps the payment domain's exceptions onto coherent, leak-free HTTP results: a provider
/// rejection the caller can act on stays a 4xx, an outage a 5xx, a not-found a 404, and an
/// illegal state transition a 409 — always with a caller-safe message, never raw SDK text.
/// </summary>
internal static class PaymentProblem
{
    public static IResult ToResult(Exception exception) => exception switch
    {
        OrderNotFoundException => Results.NotFound(new { message = exception.Message }),
        PaymentMethodNotFoundException => Results.NotFound(new { message = exception.Message }),
        PaymentChallengeException challenge =>
            Results.Json(new { message = challenge.Message, approvalUrl = challenge.ApprovalUrl }, statusCode: challenge.StatusCode),
        PaymentGatewayException gateway =>
            Results.Json(new { message = gateway.Message }, statusCode: gateway.StatusCode),
        PaymentOperationException => Results.Conflict(new { message = exception.Message }),
        ArgumentException => Results.BadRequest(new { message = exception.Message }),
        _ => Results.Json(new { message = "An unexpected error occurred while processing the payment." }, statusCode: 500)
    };
}

/// <summary>Reads the caller's identity (their username) from the validated JWT.</summary>
internal static class CallerIdentity
{
    public static string? GetBuyerId(HttpContext httpContext) => httpContext.User?.Identity?.Name;
}
