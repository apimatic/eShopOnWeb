using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription held by the signed-in shopper, as reported by the billing system of record.
/// </summary>
public class SubscriptionDto
{
    /// <summary>Identifier of the subscription in the billing system.</summary>
    public int Id { get; set; }

    /// <summary>Reference eShopOnWeb recorded on the subscription.</summary>
    public string? Reference { get; set; }

    /// <summary>Lifecycle state, e.g. <c>active</c>, <c>trialing</c>, <c>past_due</c>, <c>canceled</c>.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>True while the subscription still entitles the shopper to the plan.</summary>
    public bool IsActive { get; set; }

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    /// <summary>Recurring amount as a decimal, e.g. 299.00.</summary>
    public decimal Price { get; set; }

    public long PriceInCents { get; set; }

    public string Currency { get; set; } = string.Empty;

    public int Interval { get; set; }

    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Human readable billing cadence, e.g. "every month".</summary>
    public string BillingPeriod { get; set; } = string.Empty;

    /// <summary>When the next renewal charge is scheduled. Null when the subscription no longer renews.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    /// <summary>Outstanding balance in cents.</summary>
    public long BalanceInCents { get; set; }

    /// <summary>How the subscription is collected, e.g. <c>automatic</c> or <c>remittance</c>.</summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>Identifier of the shopper in the billing system.</summary>
    public int CustomerId { get; set; }

    /// <summary>Reference that ties the billing customer back to the eShopOnWeb user.</summary>
    public string? CustomerReference { get; set; }

    public string? CustomerEmail { get; set; }
}
