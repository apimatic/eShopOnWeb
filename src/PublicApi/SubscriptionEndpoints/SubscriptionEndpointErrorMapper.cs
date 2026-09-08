using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps Maxio integration failures to HTTP responses with client-safe messages.
/// Used by the subscription endpoints (any other exception propagates to the global middleware).
/// </summary>
internal static class SubscriptionEndpointErrorMapper
{
    public static ObjectResult ToObjectResult(Exception exception, ILogger logger)
    {
        switch (exception)
        {
            case MaxioConfigurationException configurationException:
                logger.LogError(configurationException, "Maxio integration is misconfigured.");
                return Problem(StatusCodes.Status500InternalServerError, configurationException.Message);

            case SubscriptionPlanNotFoundException notFoundException:
                logger.LogInformation(notFoundException, "A requested subscription plan is not available.");
                return Problem(StatusCodes.Status404NotFound, notFoundException.Message);

            case MaxioApiException apiException:
                return MapApiException(apiException, logger);

            default:
                throw new InvalidOperationException("Unhandled exception type passed to the subscription error mapper.", exception);
        }
    }

    private static ObjectResult MapApiException(MaxioApiException exception, ILogger logger)
    {
        // No status code -> the Maxio host could not be reached.
        if (exception.StatusCode is null)
        {
            logger.LogError(exception, "The Maxio API could not be reached.");
            return Problem(StatusCodes.Status502BadGateway,
                "The subscription billing provider could not be reached. Please try again later.");
        }

        var status = exception.StatusCode.Value;

        if (status is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
        {
            logger.LogError(exception, "Maxio rejected the configured API credentials ({StatusCode}).", (int)status);
            return Problem(StatusCodes.Status502BadGateway,
                "The subscription billing provider rejected the configured API credentials.");
        }

        if ((int)status >= 500 || status == System.Net.HttpStatusCode.TooManyRequests)
        {
            logger.LogError(exception, "Maxio reported a transient failure ({StatusCode}).", (int)status);
            return Problem(StatusCodes.Status502BadGateway,
                "The subscription billing provider reported a temporary error. Please try again later.");
        }

        // Remaining 4xx: echo the provider's validation message(s) back.
        logger.LogWarning(exception, "Maxio rejected the request ({StatusCode}).", (int)status);
        var message = exception.Errors is { Count: > 0 }
            ? string.Join(" ", exception.Errors)
            : exception.Message;
        return Problem((int)status, message, exception.Errors);
    }

    private static ObjectResult Problem(int statusCode, string message, System.Collections.Generic.IReadOnlyList<string>? errors = null)
        => new(new SubscriptionErrorPayload(message, errors))
        {
            StatusCode = statusCode
        };
}
