using System;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Maps Maxio models onto the endpoint DTOs.</summary>
public static class SubscriptionMapper
{
    public static SubscriptionPlanDto ToDto(this MaxioPlan plan) => new()
    {
        ProductId = plan.ProductId,
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        BillingInterval = plan.Interval,
        BillingIntervalUnit = plan.IntervalUnit,
        ProductFamilyHandle = plan.ProductFamilyHandle
    };

    public static SubscriptionDto ToDto(this MaxioSubscription subscription) => new()
    {
        SubscriptionId = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        PriceInCents = subscription.ProductPriceInCents,
        NextBillingDate = ParseDate(subscription.CurrentPeriodEndsAt),
        NextAssessmentAt = ParseDate(subscription.NextAssessmentAt),
        ActivatedAt = ParseDate(subscription.ActivatedAt),
        CreatedAt = ParseDate(subscription.CreatedAt),
        CanceledAt = ParseDate(subscription.CanceledAt),
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod,
        MaxioCustomerId = subscription.Customer?.Id ?? 0,
        Reference = subscription.Reference
    };

    private static DateTimeOffset? ParseDate(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
}