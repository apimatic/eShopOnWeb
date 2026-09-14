using System;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionErrorResponses
{
    public static ActionResult FromException(Exception exception, ILogger logger)
    {
        if (exception is MaxioApiException apiException)
        {
            if (apiException.StatusCode is int status
                && status is >= 400 and <= 499
                && status is not 401 and not 403 and not 404 and not 429)
            {
                return Error(status, apiException.Message);
            }

            logger.LogError(apiException, "Unexpected Maxio Billing API error.");
            return Error(StatusCodes.Status502BadGateway, "The billing provider could not complete the request. Please try again later.");
        }

        logger.LogError(exception, "Unhandled error while processing a subscription request.");
        return Error(StatusCodes.Status500InternalServerError, exception.Message);
    }

    private static ObjectResult Error(int statusCode, string message)
    {
        return new ObjectResult(new ErrorDetails { StatusCode = statusCode, Message = message })
        {
            StatusCode = statusCode
        };
    }
}
