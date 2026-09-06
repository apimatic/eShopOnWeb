using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's enrollment in a plan, as reported by the billing system of record.
/// </summary>
public class SubscriptionDto
{
    /// <summary>The subscription id in the billing system, for support and reconciliation.</summary>
    public int Id { get; set; }
    public string? Reference { get; set; }

    /// <summary>Billing state, e.g. "active", "trialing", "past_due", "canceled".</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>True while the subscription entitles the shopper to the plan.</summary>
    public bool IsLive { get; set; }

    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public decimal Price { get; set; }
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the next charge is due. Null while the subscription is not billing.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}
