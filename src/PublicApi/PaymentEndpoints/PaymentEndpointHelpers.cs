using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Shared helpers for the payment/saved-card endpoints: resolving the caller's identity from the
/// token and mapping domain/gateway failures onto coherent, leak-free HTTP responses.
/// </summary>
public static class PaymentEndpointHelpers
{
    /// <summary>The signed-in shopper's identity, taken from the JWT (never from the request body).</summary>
    public static string GetBuyerId(ClaimsPrincipal user)
    {
        var id = user.Identity?.Name
                 ?? user.FindFirstValue(ClaimTypes.Name)
                 ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(id))
        {
            throw new PaymentValidationException("The authenticated caller has no identity claim.");
        }
        return id;
    }

    /// <summary>Runs an endpoint body and converts known failures into the right status code and a safe message.</summary>
    public static async Task<IResult> Execute(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (OrderNotFoundException ex)
        {
            return Problem(StatusCodes.Status404NotFound, ex.Message);
        }
        catch (SavedPaymentMethodNotFoundException ex)
        {
            return Problem(StatusCodes.Status404NotFound, ex.Message);
        }
        catch (PaymentValidationException ex)
        {
            return Problem(StatusCodes.Status400BadRequest, ex.Message);
        }
        catch (PayPalChallengeRequiredException ex)
        {
            // The card needs browser approval; this integration does not do an approval round-trip.
            return Problem(StatusCodes.Status409Conflict, ex.Message, ex.DebugId, "challenge_required");
        }
        catch (AuthorizationNoLongerHonorableException ex)
        {
            return Problem(StatusCodes.Status409Conflict, ex.Message, ex.DebugId, "authorization_not_honorable");
        }
        catch (PayPalGatewayException ex)
        {
            var status = ex.IsClientError ? StatusCodes.Status400BadRequest : StatusCodes.Status502BadGateway;
            return Problem(status, ex.Message, ex.DebugId);
        }
    }

    private static IResult Problem(int status, string message, string? debugId = null, string? code = null) =>
        Results.Json(new PaymentProblem(status, message, code, debugId), statusCode: status);
}

/// <summary>A small, caller-safe error body shared by the payment endpoints.</summary>
public record PaymentProblem(int Status, string Message, string? Code, string? DebugId);
