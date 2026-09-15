using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Runs an endpoint body and maps the payment domain and PayPal exceptions to appropriate
/// HTTP problem responses, so each endpoint stays focused on its own logic.
/// </summary>
public static class PaymentProblem
{
    public static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (OrderNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (PaymentApprovalRequiredException ex)
        {
            // Browser approval / 3-D Secure challenge — reported, not worked around.
            return Results.Json(new { error = ex.Message, approvalRequired = true }, statusCode: StatusCodes.Status409Conflict);
        }
        catch (PaymentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (PayPalApiException ex)
        {
            // Surface a sanitized summary (never card data). A 4xx from PayPal (e.g. a declined
            // card or a validation issue) is the caller's problem → 400; a 5xx/network fault is an
            // upstream failure → 502.
            var statusCode = (int)ex.StatusCode >= 500
                ? StatusCodes.Status502BadGateway
                : StatusCodes.Status400BadRequest;
            return Results.Json(
                new { error = ex.Message, paypalDebugId = ex.DebugId, issues = ex.Issues },
                statusCode: statusCode);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status401Unauthorized);
        }
    }
}
