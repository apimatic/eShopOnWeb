using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Maps an <see cref="SmsGatewayException"/> onto a caller-facing HTTP result, keeping distinct failures
/// distinct and never leaking internal detail.
/// </summary>
public static class ProviderErrorResults
{
    public static IResult From(SmsGatewayException ex)
    {
        var status = (int?)ex.StatusCode;

        // Our credentials or our quota — the caller did nothing wrong and cannot fix it.
        if (status is 401 or 403)
        {
            return Results.Problem("The messaging provider is unavailable.", statusCode: StatusCodes.Status502BadGateway);
        }
        if (status is 429)
        {
            return Results.Problem("The messaging provider is temporarily unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        // The provider rejected the caller's request (e.g. an unusable number) — hand back that same status.
        if (status is >= 400 and < 500)
        {
            return Results.Problem(ex.Message, statusCode: status.Value);
        }

        // Transport, timeout, or a provider 5xx — no meaningful caller status.
        return Results.Problem("The messaging provider is unavailable.", statusCode: StatusCodes.Status502BadGateway);
    }
}
