using System;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.PublicApi.MaxioBilling;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionEndpointErrors
{
    public static IResult ToResult(Exception exception)
    {
        return exception switch
        {
            MaxioApiException maxioException => ToResult(maxioException),
            InvalidOperationException => Problem(StatusCodes.Status503ServiceUnavailable, exception.Message),
            _ => Problem(StatusCodes.Status500InternalServerError, exception.Message)
        };
    }

    public static IResult ToResult(MaxioApiException exception)
    {
        int statusCode = exception.StatusCode switch
        {
            var code when (int)code is >= 500 and <= 599 => StatusCodes.Status502BadGateway,
            System.Net.HttpStatusCode.NotFound => StatusCodes.Status404NotFound,
            System.Net.HttpStatusCode.Conflict => StatusCodes.Status409Conflict,
            System.Net.HttpStatusCode.UnprocessableEntity => StatusCodes.Status422UnprocessableEntity,
            System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden => StatusCodes.Status502BadGateway,
            _ => (int)exception.StatusCode
        };

        return Problem(statusCode, exception.ErrorMessage);
    }

    public static IResult Problem(int statusCode, string message)
    {
        var error = new ErrorDetails
        {
            StatusCode = statusCode,
            Message = string.IsNullOrWhiteSpace(message)
                ? "The upstream billing service reported an error."
                : message
        };

        return Results.Json(error, statusCode: statusCode);
    }
}
