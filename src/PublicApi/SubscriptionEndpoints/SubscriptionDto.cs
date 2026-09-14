using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's subscription, as it currently stands in the billing system of record.
/// </summary>
public class SubscriptionDto
{
    /// <summary>Identifier of the subscription in the billing system.</summary>
    public long Id { get; set; }

    /// <summary>Billing state, e.g. "active", "trialing", "past_due", "canceled".</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>True while the subscription still entitles the shopper to the plan.</summary>
    public bool IsLive { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    public decimal Price { get; set; }

    public long PriceInCents { get; set; }

    public int Interval { get; set; }

    public string? IntervalUnit { get; set; }

    public string? PricePointName { get; set; }

    /// <summary>When the next renewal charge is scheduled.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public DateTimeOffset? TrialEndedAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public decimal Balance { get; set; }

    public string? PaymentCollectionMethod { get; set; }

    /// <summary>Reference stored on the billing record when an idempotency key was supplied.</summary>
    public string? Reference { get; set; }

    public long CustomerId { get; set; }

    public string? CustomerReference { get; set; }

    public string? CustomerEmail { get; set; }
}
