using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Maps an <see cref="SmsGatewayException"/> to a caller-facing result. A provider rejection the
/// caller could act on (a plain 4xx) is passed through; our own credential/quota problems and
/// transport failures become 5xx. The detail is always caller-safe — never a provider body or secret.
/// </summary>
public static class SmsProviderProblem
{
    public static IResult ToResult(SmsGatewayException ex)
    {
        var status = ex.ProviderStatusCode switch
        {
            401 or 403 => StatusCodes.Status502BadGateway,   // OUR credentials — caller cannot fix
            429 => StatusCodes.Status503ServiceUnavailable,  // OUR quota
            >= 400 and < 500 => ex.ProviderStatusCode.Value, // the caller's request was rejected
            _ => StatusCodes.Status502BadGateway             // transport / unknown
        };

        return Results.Problem(
            title: "SMS provider error",
            detail: "The SMS provider could not complete the request.",
            statusCode: status);
    }
}
