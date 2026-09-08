using System;
using System.Net;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.PublicApi.Services.Maxio;
using Microsoft.eShopWeb.PublicApi.Services.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionEndpointErrorMapping
{
    public static IResult ToErrorResult(Exception exception)
    {
        switch (exception)
        {
            case UnknownPlanException unknownPlan:
                return Error(HttpStatusCode.BadRequest, unknownPlan.Message);
            case MaxioNotConfiguredException notConfigured:
                return Error(HttpStatusCode.ServiceUnavailable, notConfigured.Message);
            case MaxioApiException maxioException:
                return ToMaxioErrorResult(maxioException);
            default:
                throw exception;
        }
    }

    private static IResult ToMaxioErrorResult(MaxioApiException exception)
    {
        var status = exception.StatusCode;
        if (status == HttpStatusCode.NotFound)
        {
            return Error(HttpStatusCode.NotFound, BuildMessage(exception));
        }

        if (status == HttpStatusCode.OK ||
            status == HttpStatusCode.Unauthorized ||
            status == HttpStatusCode.Forbidden ||
            status >= HttpStatusCode.InternalServerError)
        {
            return Error(HttpStatusCode.BadGateway, BuildMessage(exception));
        }

        return Error(HttpStatusCode.BadRequest, BuildMessage(exception));
    }

    private static string BuildMessage(MaxioApiException exception)
    {
        return exception.Errors.Count > 0
            ? string.Join(" ", exception.Errors)
            : exception.Message;
    }

    private static IResult Error(HttpStatusCode statusCode, string message)
    {
        return Results.Json(new ErrorDetails
        {
            StatusCode = (int)statusCode,
            Message = message
        }, statusCode: (int)statusCode);
    }
}
