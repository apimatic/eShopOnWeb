using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string? Reference { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public string State { get; set; } = string.Empty;
    public long? PriceInCents { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }

    public static SubscriptionDto From(BillingSubscription subscription) => new()
    {
        SubscriptionId = subscription.Id,
        Reference = subscription.Reference,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        State = subscription.State,
        PriceInCents = subscription.PriceInCents,
        Price = subscription.PriceInCents / 100m,
        Currency = subscription.Currency,
        Interval = subscription.Interval,
        IntervalUnit = subscription.IntervalUnit,
        NextBillingAt = subscription.NextBillingAt,
        ActivatedAt = subscription.ActivatedAt,
        CreatedAt = subscription.CreatedAt
    };
}

/// <summary>A subscription the shopper requested whose creation Maxio has not confirmed yet.</summary>
public class PendingSubscriptionDto
{
    public string PlanHandle { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string State { get; set; } = "pending_confirmation";
    public DateTimeOffset RequestedAt { get; set; }

    public static PendingSubscriptionDto From(PendingSubscription pending) => new()
    {
        PlanHandle = pending.PlanHandle,
        Reference = pending.Reference,
        RequestedAt = pending.RequestedAt
    };
}
