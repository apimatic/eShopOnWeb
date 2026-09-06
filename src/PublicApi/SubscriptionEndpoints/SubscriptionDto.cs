using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A shopper's subscription, as held by the billing system of record.</summary>
public class SubscriptionDto
{
    /// <summary>Billing-provider identifier for the subscription.</summary>
    public long Id { get; set; }

    /// <summary>The eShopOnWeb-side reference stored on the subscription.</summary>
    public string? Reference { get; set; }

    /// <summary>Lifecycle state, e.g. <c>active</c>, <c>trialing</c>, <c>past_due</c>, <c>canceled</c>.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>True while the shopper still holds this subscription.</summary>
    public bool IsCurrent { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    /// <summary>Recurring price in minor units.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price in major units.</summary>
    public decimal Price { get; set; }

    public string? Currency { get; set; }

    public int? Interval { get; set; }

    public string? IntervalUnit { get; set; }

    public string? BillingPeriod { get; set; }

    /// <summary>When the shopper is next billed.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? TrialStartedAt { get; set; }

    public DateTimeOffset? TrialEndedAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public bool? CancelAtEndOfPeriod { get; set; }

    /// <summary>How the subscription is collected: <c>automatic</c>, <c>remittance</c>, <c>invoice</c> or <c>prepaid</c>.</summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>Outstanding balance in minor units.</summary>
    public long BalanceInCents { get; set; }

    /// <summary>Billing-provider customer this subscription belongs to.</summary>
    public long CustomerId { get; set; }

    public string? CustomerReference { get; set; }
}
