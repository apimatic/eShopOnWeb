using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Maps payment domain exceptions to appropriate HTTP results.</summary>
public static class PaymentResults
{
    public static IResult FromException(PaymentException ex) => ex switch
    {
        // A required browser approval (e.g. 3-D Secure) — surfaced, not worked around.
        PaymentChallengeRequiredException => Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status422UnprocessableEntity),
        _ => Results.BadRequest(new { message = ex.Message })
    };

    public static IResult NotFound(PaymentNotFoundException ex) =>
        Results.NotFound(new { message = ex.Message });
}
