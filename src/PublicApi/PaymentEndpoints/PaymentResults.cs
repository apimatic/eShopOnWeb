using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.PublicApi.Services;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public static class PaymentResults
{
    public static IResult FromException(PaymentOperationException exception)
    {
        var statusCode = exception.Kind switch
        {
            PaymentFailureKind.Validation => StatusCodes.Status400BadRequest,
            PaymentFailureKind.NotFound => StatusCodes.Status404NotFound,
            PaymentFailureKind.Upstream => StatusCodes.Status502BadGateway,
            _ => StatusCodes.Status409Conflict
        };

        return Results.Problem(
            detail: exception.Message,
            statusCode: statusCode,
            title: "Payment operation failed",
            extensions: new Dictionary<string, object?>
            {
                ["finixCode"] = exception.FinixCode
            });
    }

    public static IResult Unauthorized() => Results.Unauthorized();
}
