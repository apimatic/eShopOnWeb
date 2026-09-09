using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Maps a <see cref="PaymentGatewayException"/> to a caller-facing HTTP result. Follows the boundary
/// rule: caller-actionable provider rejections (bad input / declined) surface as 4xx; our own
/// credential/quota/transport failures surface as 5xx. Only the caller-safe message is returned.
/// </summary>
public static class PaymentResults
{
    public static IResult FromGatewayException(PaymentGatewayException ex)
    {
        var status = ex switch
        {
            PaymentChallengeRequiredException => StatusCodes.Status402PaymentRequired,
            PaymentReauthorizationException => StatusCodes.Status409Conflict,
            { StatusCode: 400 or 422 } => StatusCodes.Status400BadRequest,
            { StatusCode: 402 } => StatusCodes.Status402PaymentRequired,
            { StatusCode: 409 } => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status502BadGateway
        };

        return Results.Json(new
        {
            statusCode = status,
            message = ex.Message,
            debugId = ex.DebugId
        }, statusCode: status);
    }
}
