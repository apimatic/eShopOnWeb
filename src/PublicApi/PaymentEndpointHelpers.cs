using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi;

public static class PaymentEndpointHelpers
{
    public static string GetBuyerId(HttpContext context)
    {
        var user = context.User;
        var buyerId = user.Identity?.Name;

        if (string.IsNullOrWhiteSpace(buyerId))
        {
            buyerId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        }

        if (string.IsNullOrWhiteSpace(buyerId))
        {
            buyerId = user.FindFirstValue("sub");
        }

        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentOperationException(PaymentErrorKind.Validation,
                "The bearer token does not contain a shopper identity.");
        }

        return buyerId;
    }

    public static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (PaymentOperationException ex)
        {
            return Results.Json(new
            {
                error = ex.Message,
                operatorAction = ex.OperatorAction
            }, statusCode: StatusCodeFor(ex.Kind));
        }
        catch (FinixGatewayException ex)
        {
            return Results.Json(new
            {
                error = ex.Message,
                operatorAction = ex.IsHoldNotCapturable
                    ? "The hold cannot be captured. Ask the shopper to authorize payment again."
                    : null
            }, statusCode: ex.IsDecline ? StatusCodes.Status402PaymentRequired
                : ex.IsHoldNotCapturable ? StatusCodes.Status409Conflict
                : StatusCodes.Status502BadGateway);
        }
    }

    private static int StatusCodeFor(PaymentErrorKind kind) => kind switch
    {
        PaymentErrorKind.Validation => StatusCodes.Status400BadRequest,
        PaymentErrorKind.NotFound => StatusCodes.Status404NotFound,
        PaymentErrorKind.Conflict => StatusCodes.Status409Conflict,
        PaymentErrorKind.PaymentDeclined => StatusCodes.Status402PaymentRequired,
        PaymentErrorKind.HoldNotCapturable => StatusCodes.Status409Conflict,
        PaymentErrorKind.GatewayUnavailable => StatusCodes.Status502BadGateway,
        _ => StatusCodes.Status500InternalServerError
    };
}
