using System;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Shared helpers for the subscription endpoints: resolving the caller's identity from the JWT, and
/// mapping domain types and failures to HTTP responses with a single, coherent status ladder.
/// </summary>
internal static class SubscriptionEndpointHelpers
{
    /// <summary>Builds the subscriber identity from the authenticated principal, or null when absent.</summary>
    public static SubscriberIdentity? ResolveSubscriber(ClaimsPrincipal? principal)
    {
        var userName = principal?.FindFirstValue(ClaimTypes.Name) ?? principal?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
            return null;

        // In eShopOnWeb the username is the user's email; it is stable and used as the Maxio customer reference.
        return new SubscriberIdentity(userReference: userName, email: userName);
    }

    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Id = plan.Id,
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        RequiresPaymentMethod = plan.RequiresPaymentMethod,
    };

    public static CustomerSubscriptionDto ToDto(this CustomerSubscription sub) => new()
    {
        Id = sub.Id,
        Reference = sub.Reference,
        PlanHandle = sub.PlanHandle,
        PlanName = sub.PlanName,
        State = sub.State,
        PriceInCents = sub.PriceInCents,
        NextBillingDate = sub.NextBillingDate,
        CreatedAt = sub.CreatedAt,
    };

    /// <summary>
    /// Maps a <see cref="SubscriptionBillingException"/> to a caller-facing problem response. Our own
    /// credentials/quota problems (401/403/429) are never the caller's fault, so they surface as 5xx;
    /// a provider rejection the caller can act on (other 4xx) is passed through; transport/unknown
    /// failures surface as 502. The message is always the caller-safe provider message, never a raw
    /// SDK/framework exception string.
    /// </summary>
    public static IResult ToProblem(this SubscriptionBillingException ex)
    {
        var (status, message) = ((int?)ex.StatusCode) switch
        {
            401 or 403 => (StatusCodes.Status502BadGateway, "The billing provider is unavailable."),
            429 => (StatusCodes.Status503ServiceUnavailable, "The billing provider is temporarily unavailable. Please try again shortly."),
            >= 400 and < 500 => ((int)ex.StatusCode!, ex.ProviderMessage),
            _ => (StatusCodes.Status502BadGateway, ex.ProviderMessage),
        };

        return Results.Problem(detail: message, statusCode: status, title: "Subscription billing error");
    }
}
