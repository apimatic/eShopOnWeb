using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

internal static class EndpointErrors
{
    public static IResult FromProvider(SmsProviderException exception)
    {
        var status = (int?)exception.StatusCode;
        if (status == 429)
        {
            return Results.Json(new { message = exception.Message }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Json(new { message = exception.Message }, statusCode: StatusCodes.Status502BadGateway);
    }

    public static IResult FromException(System.Exception exception) => exception switch
    {
        KeyNotFoundException => Results.NotFound(new { message = exception.Message }),
        ArgumentException => Results.BadRequest(new { message = exception.Message }),
        InvalidContactNumberException => Results.BadRequest(new { message = exception.Message }),
        OrderStateException => Results.Conflict(new { message = exception.Message }),
        SmsProviderException provider => FromProvider(provider),
        _ => Results.Problem(exception.Message)
    };
}
