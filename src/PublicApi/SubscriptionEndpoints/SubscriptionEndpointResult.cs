using System;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Shared translation of integration failures into HTTP responses for the subscription endpoints.
/// Only caller-safe messages ever reach the wire; details go to the logs.
/// </summary>
internal static class SubscriptionEndpointResult
{
    public static IResult From(MaxioBillingException exception)
    {
        return Results.Problem(
            statusCode: exception.StatusCode,
            title: "Subscription request failed",
            detail: exception.Message);
    }

    public static IResult FromUnexpected(Exception exception, ILogger logger, string operation)
    {
        logger.LogError(exception, "Unexpected error while {Operation}.", operation);
        return Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Subscription request failed",
            detail: "An unexpected error occurred while processing your request. Please try again later.");
    }
}
