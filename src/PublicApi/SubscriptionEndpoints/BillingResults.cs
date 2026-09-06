using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Single mapping from a billing failure to an HTTP response, applied identically by every subscription
/// endpoint so that the same kind of failure always looks the same to a caller.
/// <para>
/// Only <see cref="BillingException.Message"/> — which the billing adapter guarantees is caller-safe —
/// crosses the wire. Provider bodies and exception detail stay in the log.
/// </para>
/// </summary>
internal static class BillingResults
{
    public static IResult Problem(BillingException exception, Guid correlationId)
    {
        var (status, title) = Describe(exception.Kind);

        var extensions = new Dictionary<string, object?>
        {
            ["correlationId"] = correlationId
        };

        if (exception.ProviderMessages.Count > 0)
        {
            extensions["billingMessages"] = exception.ProviderMessages;
        }

        return Results.Problem(
            detail: exception.Message,
            statusCode: (int)status,
            title: title,
            extensions: extensions);
    }

    public static IResult Unauthenticated() => Results.Problem(
        detail: "The access token does not identify a shopper.",
        statusCode: (int)HttpStatusCode.Unauthorized,
        title: "Not authenticated");

    private static (HttpStatusCode Status, string Title) Describe(BillingFailureKind kind) => kind switch
    {
        // A caller can act on these.
        BillingFailureKind.InvalidRequest => (HttpStatusCode.BadRequest, "Invalid subscription request"),
        BillingFailureKind.NotFound => (HttpStatusCode.NotFound, "Not found"),
        BillingFailureKind.Rejected => (HttpStatusCode.UnprocessableEntity, "Subscription rejected"),

        // These are ours or the provider's, never the caller's.
        BillingFailureKind.Configuration => (HttpStatusCode.ServiceUnavailable, "Subscription billing unavailable"),
        BillingFailureKind.Unavailable => (HttpStatusCode.ServiceUnavailable, "Subscription billing unavailable"),

        // The request may have taken effect: retrying blindly is exactly the wrong move, so say so.
        BillingFailureKind.OutcomeUnknown => (HttpStatusCode.BadGateway, "Subscription outcome unconfirmed"),

        _ => (HttpStatusCode.BadGateway, "Subscription billing error")
    };
}
