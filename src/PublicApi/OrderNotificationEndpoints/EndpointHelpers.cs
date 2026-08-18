using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

internal static class CallerIdentity
{
    /// <summary>The signed-in caller's identity (username) from the token, or null if absent.</summary>
    public static string? BuyerId(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.Name);
}

internal static class GatewayErrorMapper
{
    /// <summary>
    /// Map a provider failure to a caller-facing status: OUR-fault statuses (401/403/429) never surface as
    /// the caller's; a provider 4xx the caller can act on is passed through; everything else is a 502.
    /// The message is already caller-safe (no phone number, no secret).
    /// </summary>
    public static IResult Map(SmsGatewayException ex)
    {
        var status = ex.StatusCode switch
        {
            401 or 403 => StatusCodes.Status502BadGateway,   // our credentials — caller can't fix it
            429 => StatusCodes.Status503ServiceUnavailable,  // our quota — not the caller's fault
            >= 400 and < 500 => ex.StatusCode!.Value,        // the caller's request was rejected
            _ => StatusCodes.Status502BadGateway             // transport / provider 5xx / unknown
        };
        return Results.Problem(detail: ex.Message, statusCode: status);
    }
}
