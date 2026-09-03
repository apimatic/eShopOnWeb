using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Shared helpers for the payment endpoints: reading the caller's identity from the JWT and
/// translating domain / gateway failures into HTTP results with caller-safe messages.
/// </summary>
public static class PaymentApiHelpers
{
    /// <summary>The signed-in shopper's id (username) from the token, or null if unauthenticated.</summary>
    public static string? GetBuyerId(HttpContext httpContext) =>
        httpContext.User.FindFirst(ClaimTypes.Name)?.Value ?? httpContext.User.Identity?.Name;

    /// <summary>Runs a shopper-scoped action, resolving the caller id and mapping failures to HTTP results.</summary>
    public static async Task<IResult> RunAsync(HttpContext httpContext, Func<string, Task<IResult>> action)
    {
        var buyerId = GetBuyerId(httpContext);
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        return await ExecuteAsync(() => action(buyerId));
    }

    /// <summary>Runs an operator action (no caller-id scoping), mapping failures to HTTP results.</summary>
    public static Task<IResult> RunAsync(Func<Task<IResult>> action) => ExecuteAsync(action);

    public static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (PaymentChallengeRequiredException ex)
        {
            // We deliberately do not build a browser approval round-trip; surface the condition.
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status402PaymentRequired,
                title: "Payment requires shopper approval");
        }
        catch (PaymentGatewayException ex)
        {
            var statusCode = ex.IsClientError
                ? ex.StatusCode ?? StatusCodes.Status400BadRequest
                : StatusCodes.Status502BadGateway;
            return Results.Problem(detail: ex.Message, statusCode: statusCode, title: "Payment provider error");
        }
        catch (PaymentOperationException ex)
        {
            var statusCode = ex.Error switch
            {
                PaymentOperationError.NotFound => StatusCodes.Status404NotFound,
                PaymentOperationError.Validation => StatusCodes.Status400BadRequest,
                PaymentOperationError.Conflict => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest
            };
            return Results.Problem(detail: ex.Message, statusCode: statusCode);
        }
    }

    /// <summary>
    /// Builds a shipping address from the request, filling non-empty placeholders where the shopper
    /// did not supply one (the payment flow does not require a real shipping address).
    /// </summary>
    public static Address BuildAddress(AddressDto? address) => new(
        NonEmpty(address?.Street, "N/A"),
        NonEmpty(address?.City, "N/A"),
        NonEmpty(address?.State, "N/A"),
        NonEmpty(address?.Country, "N/A"),
        NonEmpty(address?.ZipCode, "00000"));

    private static string NonEmpty(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value!.Trim();
}
