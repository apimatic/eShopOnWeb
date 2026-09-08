using System;
using Microsoft.eShopWeb.PublicApi.SubscriptionBilling;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's Maxio subscription as returned by the subscription endpoints.
/// </summary>
public class SubscriptionDto
{
    public long SubscriptionId { get; set; }

    /// <summary>Current Maxio subscription state, e.g. "active".</summary>
    public string State { get; set; } = string.Empty;

    public string? Reference { get; set; }

    public string PlanHandle { get; set; } = string.Empty;

    public string? PlanName { get; set; }

    public long PriceInCents { get; set; }

    public decimal Price { get; set; }

    public int? Interval { get; set; }

    public string? IntervalUnit { get; set; }

    public string? Currency { get; set; }

    /// <summary>Next billing date for the subscription, when one is scheduled.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset? PeriodStartedAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public static SubscriptionDto FromSubscription(CustomerSubscription subscription) => new()
    {
        SubscriptionId = subscription.SubscriptionId,
        State = subscription.State,
        Reference = subscription.Reference,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        Price = subscription.PriceInCents / 100m,
        Interval = subscription.Interval,
        IntervalUnit = subscription.IntervalUnit,
        Currency = subscription.Currency,
        NextBillingDate = subscription.NextBillingAt,
        PeriodStartedAt = subscription.PeriodStartedAt,
        ActivatedAt = subscription.ActivatedAt,
        CreatedAt = subscription.CreatedAt,
        UpdatedAt = subscription.UpdatedAt
    };
}
