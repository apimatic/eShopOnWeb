using System;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Maps subscription domain models to API DTOs and domain exceptions to HTTP problem results.</summary>
internal static class SubscriptionMapping
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.Price,
        Interval = plan.IntervalUnit,
        IntervalCount = plan.IntervalCount,
        RequiresPaymentMethod = plan.RequiresPaymentMethod,
    };

    public static SubscriptionDto ToDto(this SubscriptionSummary summary) => new()
    {
        Id = summary.Id,
        State = summary.State,
        PlanHandle = summary.PlanHandle,
        PlanName = summary.PlanName,
        Price = summary.Price,
        Interval = summary.IntervalUnit,
        IntervalCount = summary.IntervalCount,
        NextBillingDate = summary.NextBillingDate,
        PaymentCollectionMethod = summary.PaymentCollectionMethod,
    };

    /// <summary>
    /// Translates known subscription-domain exceptions into HTTP problem responses. Returns null for
    /// unknown exceptions so they propagate to the global exception middleware.
    /// </summary>
    public static IResult? TryToProblem(Exception exception) => exception switch
    {
        SubscriptionPlanNotFoundException => Results.Problem(
            detail: exception.Message, statusCode: StatusCodes.Status400BadRequest, title: "Invalid subscription plan"),
        SubscriptionConfigurationException => Results.Problem(
            detail: exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable, title: "Subscription billing not configured"),
        SubscriptionBillingException => Results.Problem(
            detail: exception.Message, statusCode: StatusCodes.Status502BadGateway, title: "Billing provider error"),
        _ => null,
    };
}
