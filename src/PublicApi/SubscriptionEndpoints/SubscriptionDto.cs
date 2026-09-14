using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's enrollment in a subscription plan, as reported by the billing system of record.
/// </summary>
public class SubscriptionDto
{
    /// <summary>Identifier of the subscription in the billing system.</summary>
    public long Id { get; set; }

    /// <summary>The reference this application assigned to the subscription.</summary>
    public string? Reference { get; set; }

    /// <summary>Lifecycle state, e.g. <c>active</c>, <c>trialing</c>, <c>past_due</c>, <c>canceled</c>.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>True while the subscription still entitles the shopper to the plan.</summary>
    public bool IsLive { get; set; }

    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;

    /// <summary>Recurring price in whole currency units, e.g. 299.00.</summary>
    public decimal Price { get; set; }

    public long PriceInCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the next renewal will be billed.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>How the renewal is collected, e.g. <c>remittance</c> for invoiced billing.</summary>
    public string? PaymentCollectionMethod { get; set; }
}
