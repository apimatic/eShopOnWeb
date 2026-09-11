using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Shared helpers for the payment endpoints: identity extraction and exception→HTTP mapping.</summary>
public static class PaymentApiHelpers
{
    /// <summary>The caller's shopper identity (JWT name claim), or null if unauthenticated.</summary>
    public static string? GetBuyerId(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;

    /// <summary>
    /// Runs an endpoint handler and maps domain/provider exceptions to coherent, caller-safe HTTP
    /// results. Provider internals are never surfaced; PaymentProcessingException messages are already
    /// caller-safe. Applied identically at every call site.
    /// </summary>
    public static async Task<IResult> ExecuteAsync(Func<Task<IResult>> handler)
    {
        try
        {
            return await handler();
        }
        catch (PaymentNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (PaymentApprovalRequiredException ex)
        {
            // A browser-approval challenge — not something this integration completes.
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status409Conflict);
        }
        catch (PaymentStateException ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (PaymentProcessingException ex)
        {
            var (status, message) = MapProvider(ex);
            return Results.Json(new { error = message, outcomeUnknown = ex.OutcomeUnknown },
                statusCode: status);
        }
    }

    private static (int Status, string Message) MapProvider(PaymentProcessingException ex)
    {
        // Our credentials / our quota — the caller did nothing wrong and cannot fix it: mask as 5xx.
        if (ex.StatusCode is 401 or 403) return (StatusCodes.Status502BadGateway, "Payment provider is unavailable.");
        if (ex.StatusCode is 429) return (StatusCodes.Status503ServiceUnavailable, "Payment provider is temporarily unavailable.");

        // The provider rejected the payment (declined card, validation) — caller can act on it.
        if (ex.StatusCode is >= 400 and < 500)
            return (StatusCodes.Status402PaymentRequired, ex.Message);

        // Transport, timeout, or provider 5xx — no meaningful caller status.
        return (StatusCodes.Status502BadGateway, ex.Message);
    }
}
