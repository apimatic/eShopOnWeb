using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Shared helpers for the payment endpoints: caller identity and a coherent error boundary that
/// keeps distinct failures distinct and never leaks internal detail.</summary>
public static class PaymentEndpointHelpers
{
    /// <summary>The caller's buyer id (username) from the JWT, or null when absent.</summary>
    public static string? BuyerId(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.Name);

    /// <summary>Runs an endpoint body, translating known failures into coherent HTTP results.</summary>
    public static async Task<IResult> Guarded(ClaimsPrincipal user, Func<string, Task<IResult>> action)
    {
        var buyerId = BuyerId(user);
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        try
        {
            return await action(buyerId);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    /// <summary>Operator variant with no buyer scoping.</summary>
    public static async Task<IResult> Guarded(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    public static IResult MapException(Exception ex) => ex switch
    {
        PaymentNotFoundException e => Problem(StatusCodes.Status404NotFound, e.Message),
        InvalidPaymentOperationException e => Problem(StatusCodes.Status400BadRequest, e.Message),
        PayPalChallengeRequiredException e => Problem(StatusCodes.Status409Conflict, e.Message, "challenge_required"),
        PayPalAuthorizationExpiredException e => Problem(StatusCodes.Status409Conflict, e.Message, e.Issue, e.DebugId),
        PayPalGatewayException e => Problem(GatewayStatus(e), e.Message, e.Issue, e.DebugId),
        _ => Problem(StatusCodes.Status500InternalServerError, "An unexpected error occurred.")
    };

    // Map the provider's status to a caller-facing one: our-fault (401/403) and rate-limit (429) are 5xx to
    // the caller; a genuine caller 4xx (e.g. a declined card, 422) is passed through; transport/5xx → 502.
    private static int GatewayStatus(PayPalGatewayException e) => e.StatusCode switch
    {
        401 or 403 => StatusCodes.Status502BadGateway,
        429 => StatusCodes.Status503ServiceUnavailable,
        >= 400 and < 500 => e.StatusCode!.Value,
        _ => StatusCodes.Status502BadGateway
    };

    private static IResult Problem(int status, string message, string? issue = null, string? debugId = null) =>
        Results.Json(new { status, message, issue, debugId }, statusCode: status);
}
