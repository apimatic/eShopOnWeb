using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Maps the payment layer's exceptions to caller-safe HTTP responses. A provider 4xx the caller can
/// act on is surfaced as that status; our own credential/quota problems and transport failures
/// surface as 5xx so a caller is never told they are at fault when they are not.
/// </summary>
public static class PaymentProblem
{
    /// <summary>Run an endpoint handler, converting known payment failures to HTTP responses.</summary>
    public static async Task<IResult> RunAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (IsHandled(ex))
        {
            return Map(ex);
        }
    }

    public static bool IsHandled(Exception ex) =>
        ex is PaymentNotFoundException or PaymentValidationException or PaymentConflictException or PayPalIntegrationException;

    public static IResult Map(Exception ex) => ex switch
    {
        PaymentNotFoundException => Problem(StatusCodes.Status404NotFound, ex.Message),
        PaymentValidationException => Problem(StatusCodes.Status400BadRequest, ex.Message),
        PaymentConflictException => Problem(StatusCodes.Status409Conflict, ex.Message),
        PayerActionRequiredException p => Problem(StatusCodes.Status422UnprocessableEntity, p.Message, p.DebugId),
        PayPalIntegrationException p => MapPayPal(p),
        _ => Problem(StatusCodes.Status500InternalServerError, "Unexpected error."),
    };

    private static IResult MapPayPal(PayPalIntegrationException ex)
    {
        if (ex.OutcomeUnknown)
        {
            return Problem(StatusCodes.Status502BadGateway, ex.Message, ex.DebugId);
        }

        var code = (int?)ex.StatusCode;
        return code switch
        {
            401 or 403 => Problem(StatusCodes.Status502BadGateway, "The payment provider is unavailable.", ex.DebugId),
            429 => Problem(StatusCodes.Status503ServiceUnavailable, "The payment provider is temporarily unavailable.", ex.DebugId),
            >= 400 and < 500 => Problem(code.Value, ex.Message, ex.DebugId),
            _ => Problem(StatusCodes.Status502BadGateway, ex.Message, ex.DebugId),
        };
    }

    private static IResult Problem(int status, string message, string? debugId = null) =>
        Results.Json(new PaymentProblemBody(status, message, debugId), statusCode: status);

    private sealed record PaymentProblemBody(int Status, string Message, string? DebugId);
}
