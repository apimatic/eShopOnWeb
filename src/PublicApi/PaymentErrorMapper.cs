using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Maps payment-domain failures onto HTTP results, keeping provider status
/// distinctions intact and internal exception detail off the wire.
/// </summary>
public static class PaymentErrorMapper
{
    public static IResult ToErrorResult(PaymentGatewayException ex) => ex.Kind switch
    {
        PaymentGatewayErrorKind.Validation => Results.UnprocessableEntity(new { message = ex.Message }),
        PaymentGatewayErrorKind.NotFound => Results.NotFound(new { message = ex.Message }),
        PaymentGatewayErrorKind.Conflict => Results.Conflict(new { message = ex.Message }),
        PaymentGatewayErrorKind.PayerActionRequired => Results.UnprocessableEntity(new { message = ex.Message }),
        PaymentGatewayErrorKind.Unavailable => Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status502BadGateway),
        _ => Results.Json(new { message = "The payment could not be completed." }, statusCode: StatusCodes.Status500InternalServerError)
    };
}
