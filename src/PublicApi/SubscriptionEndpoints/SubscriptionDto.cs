using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's subscription, as the billing system currently reports it.
/// </summary>
public class SubscriptionDto
{
    /// <summary>Identifier of the subscription in the billing system.</summary>
    public long Id { get; set; }

    /// <summary>Lifecycle state, e.g. <c>active</c>, <c>past_due</c>, <c>canceled</c>.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>True while this subscription still entitles the shopper to the plan.</summary>
    public bool IsLive { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    /// <summary>The recurring price this subscription is billed at.</summary>
    public decimal? Price { get; set; }

    public string? Currency { get; set; }

    public int? IntervalLength { get; set; }

    public string? IntervalUnit { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    /// <summary>When the billing system will next bill this subscription.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    /// <summary>How payment is collected, e.g. <c>remittance</c> or <c>automatic</c>.</summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>Identifier of the shopper's customer record in the billing system.</summary>
    public long BillingCustomerId { get; set; }
}
