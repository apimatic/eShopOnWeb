using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Shared helpers for the payment endpoints: caller identity and error mapping.</summary>
public static class PaymentProblem
{
    /// <summary>The signed-in shopper's id (their username/email from the JWT).</summary>
    public static string? BuyerId(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;

    /// <summary>Maps a domain/PayPal exception to an appropriate HTTP result so callers get a
    /// clear, actionable status rather than an opaque 500.</summary>
    public static IResult ToResult(Exception ex) => ex switch
    {
        OrderNotFoundException => Results.NotFound(new { message = ex.Message }),
        PaymentChallengeRequiredException => Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status422UnprocessableEntity),
        AuthorizationUnrenewableException => Results.Conflict(new { message = ex.Message }),
        PaymentException => Results.BadRequest(new { message = ex.Message }),
        PayPalApiException p => Results.Json(
            new { message = p.Message, issue = p.Issue, name = p.Name, debugId = p.DebugId },
            statusCode: StatusCodes.Status502BadGateway),
        _ => Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status500InternalServerError)
    };
}
