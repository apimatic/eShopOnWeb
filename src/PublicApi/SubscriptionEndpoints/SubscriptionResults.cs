using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Translates subscription failures into HTTP responses, so every subscription endpoint
/// answers the same way for the same kind of problem.
/// </summary>
internal static class SubscriptionResults
{
    public static IResult Problem<T>(SubscriptionResult<T> result)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException("A successful result has no problem to report.");
        }

        var (statusCode, title) = Describe(result.Failure);

        var extensions = new Dictionary<string, object?>
        {
            ["failure"] = result.Failure.ToString()
        };

        if (result.Errors.Any())
        {
            extensions["errors"] = result.Errors;
        }

        return Results.Problem(
            detail: result.Message,
            statusCode: statusCode,
            title: title,
            extensions: extensions);
    }

    private static (int StatusCode, string Title) Describe(SubscriptionFailure failure) => failure switch
    {
        // The deployment is missing its billing configuration; a caller retrying will not help,
        // but this is an operational problem rather than a bad request.
        SubscriptionFailure.NotConfigured => (StatusCodes.Status503ServiceUnavailable, "Subscription billing is unavailable"),
        SubscriptionFailure.InvalidRequest => (StatusCodes.Status400BadRequest, "Invalid subscription request"),
        SubscriptionFailure.PlanNotFound => (StatusCodes.Status404NotFound, "Subscription plan not found"),
        SubscriptionFailure.Conflict => (StatusCodes.Status409Conflict, "Subscription request conflict"),

        // The billing system understood the request and refused it. Passed through as 422 so the
        // caller can tell "you asked for something invalid" apart from "we are broken".
        SubscriptionFailure.UpstreamRejected => (StatusCodes.Status422UnprocessableEntity, "Subscription rejected by the billing system"),
        SubscriptionFailure.UpstreamUnavailable => (StatusCodes.Status502BadGateway, "The billing system is unavailable"),
        _ => (StatusCodes.Status500InternalServerError, "Subscription request failed")
    };
}
