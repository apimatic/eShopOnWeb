using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Services;

namespace Microsoft.eShopWeb.PublicApi;

public static class EndpointHelpers
{
    public static string GetOwnerId(ClaimsPrincipal user)
    {
        var ownerId = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            throw new PaymentOperationException(
                PaymentOperationError.Forbidden,
                "The authenticated caller identity was not available.");
        }

        return ownerId;
    }

    public static IResult ToResult(PaymentOperationException exception)
    {
        var statusCode = exception.Error switch
        {
            PaymentOperationError.Validation => StatusCodes.Status400BadRequest,
            PaymentOperationError.NotFound => StatusCodes.Status404NotFound,
            PaymentOperationError.Forbidden => StatusCodes.Status403Forbidden,
            PaymentOperationError.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status502BadGateway
        };

        return Results.Json(new { error = exception.Message }, statusCode: statusCode);
    }
}
