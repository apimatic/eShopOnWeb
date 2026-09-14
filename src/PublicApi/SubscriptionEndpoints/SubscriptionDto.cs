using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's subscription as it stands in the billing system of record.
/// </summary>
public class SubscriptionDto
{
    public long Id { get; set; }

    /// <summary>Deterministic reference eShopOnWeb assigns; also the idempotency key for subscribing.</summary>
    public string? Reference { get; set; }

    /// <summary>Billing-system state, e.g. active / trialing / past_due / canceled.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>True while the subscription is still a going concern.</summary>
    public bool IsLive { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    public decimal PlanPrice { get; set; }

    public long PlanPriceInCents { get; set; }

    public string Currency { get; set; } = string.Empty;

    public int? Interval { get; set; }

    public string? IntervalUnit { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the next charge is expected.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    /// <summary>Outstanding balance on the subscription, in <see cref="Currency"/>.</summary>
    public decimal Balance { get; set; }

    public string? PaymentCollectionMethod { get; set; }

    public long CustomerId { get; set; }

    public string? CustomerReference { get; set; }
}
