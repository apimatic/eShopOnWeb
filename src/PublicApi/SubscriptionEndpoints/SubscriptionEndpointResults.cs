using System;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionEndpointResults
{
    public static IResult FromException(
        Subscriptions.SubscriptionBillingException exception,
        HttpContext httpContext,
        ILogger logger)
    {
        logger.LogWarning(
            exception,
            "Subscription billing failed with {Error}; provider status {ProviderStatus}; trace {TraceId}.",
            exception.Error,
            exception.ProviderStatus is null ? null : (int)exception.ProviderStatus,
            httpContext.TraceIdentifier);

        var status = exception.Error switch
        {
            Subscriptions.SubscriptionBillingError.InvalidRequest => StatusCodes.Status400BadRequest,
            Subscriptions.SubscriptionBillingError.NotFound => StatusCodes.Status404NotFound,
            Subscriptions.SubscriptionBillingError.Conflict => StatusCodes.Status409Conflict,
            Subscriptions.SubscriptionBillingError.InvalidProviderResponse => StatusCodes.Status502BadGateway,
            Subscriptions.SubscriptionBillingError.ProviderUnavailable => StatusCodes.Status503ServiceUnavailable,
            Subscriptions.SubscriptionBillingError.UnknownWriteOutcome => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError
        };

        return Results.Problem(
            statusCode: status,
            title: "Subscription billing request failed",
            detail: exception.Message,
            extensions: new System.Collections.Generic.Dictionary<string, object?>
            {
                ["traceId"] = httpContext.TraceIdentifier
            });
    }
}
