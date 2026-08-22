using System;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

internal static class PaymentHttp
{
    public static string BuyerId(HttpContext http)
    {
        var name = http.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new PaymentException("The access token does not contain a user name.", 401);
        }

        return name;
    }

    public static IResult FromException(Exception ex)
    {
        if (ex is PayerActionRequiredException payer)
        {
            return Results.Json(new
            {
                message = payer.Message,
                paypalOrderId = payer.PayPalOrderId,
                paypalDebugId = payer.PayPalDebugId
            }, statusCode: payer.StatusCode);
        }

        if (ex is PaymentException payment)
        {
            return Results.Json(new { message = payment.Message }, statusCode: payment.StatusCode);
        }

        throw ex;
    }
}
