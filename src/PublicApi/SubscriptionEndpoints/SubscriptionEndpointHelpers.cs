using System;
using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionEndpointHelpers
{
    /// <summary>
    /// Builds the subscriber from the bearer token, and from nothing else. The caller never states who
    /// they are in the request body, so one user cannot enrol — or read the subscriptions of — another.
    /// </summary>
    public static SubscriberIdentity? ResolveSubscriber(ClaimsPrincipal user, string? firstName = null, string? lastName = null)
    {
        var userName = user.Identity?.Name
            ?? user.FindFirstValue(ClaimTypes.Name)
            ?? user.FindFirstValue("unique_name");

        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var email = user.FindFirstValue(ClaimTypes.Email)
            ?? user.FindFirstValue("email")
            ?? userName;

        return new SubscriberIdentity(userName, email, firstName, lastName);
    }

    /// <summary>
    /// Converts a billing failure into an HTTP response.
    /// </summary>
    /// <remarks>
    /// One ladder, applied identically by all three endpoints, so the same failure never means different
    /// things on different routes. Failures the caller can act on keep a 4xx; a failure of ours, or of
    /// the provider, becomes a 5xx — collapsing both into one status would throw away the only signal
    /// that separates "your request was wrong" from "billing is down". The message is the caller-safe
    /// one built at the integration boundary; no provider or framework exception text reaches the wire.
    /// </remarks>
    public static IResult ToProblem(BillingException exception)
    {
        var (statusCode, title) = exception.Kind switch
        {
            BillingFailureKind.NotConfigured => (StatusCodes.Status503ServiceUnavailable, "Subscription billing is not available"),
            BillingFailureKind.NotFound => (StatusCodes.Status404NotFound, "Not found"),
            BillingFailureKind.Rejected => (StatusCodes.Status422UnprocessableEntity, "The billing system rejected this request"),
            BillingFailureKind.Conflict => (StatusCodes.Status409Conflict, "Conflicting billing record"),
            BillingFailureKind.Unauthenticated => (StatusCodes.Status502BadGateway, "Billing credentials were rejected"),
            BillingFailureKind.Unavailable => (StatusCodes.Status503ServiceUnavailable, "The billing system is unavailable"),
            BillingFailureKind.UnknownOutcome => (StatusCodes.Status502BadGateway, "The billing request could not be confirmed"),
            BillingFailureKind.InvalidResponse => (StatusCodes.Status502BadGateway, "The billing system returned an unusable response"),
            _ => (StatusCodes.Status500InternalServerError, "Billing failed")
        };

        var extensions = new Dictionary<string, object?>
        {
            ["billingFailure"] = exception.Kind.ToString()
        };

        if (exception.ProviderMessages.Count > 0)
        {
            // Provider validation messages verbatim: this is where a rejection such as a required
            // payment method shows up, and summarising it would make that undiagnosable.
            extensions["billingMessages"] = exception.ProviderMessages;
        }

        return Results.Problem(detail: exception.Message, statusCode: statusCode, title: title, extensions: extensions);
    }

    public static IResult Unauthenticated() =>
        Results.Problem(
            detail: "The bearer token does not identify a user.",
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Not authenticated");

    public static IResult BadRequest(string detail) =>
        Results.Problem(detail: detail, statusCode: StatusCodes.Status400BadRequest, title: "Invalid request");
}
