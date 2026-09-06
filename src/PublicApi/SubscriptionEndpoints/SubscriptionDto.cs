using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's subscription, as recorded by the billing system.
/// </summary>
public class SubscriptionDto
{
    /// <summary>Billing-system identifier of the subscription.</summary>
    public int Id { get; set; }

    /// <summary>e.g. "active", "trialing", "past_due", "canceled".</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>True while the subscription still entitles the shopper to the plan.</summary>
    public bool IsActive { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    /// <summary>The recurring amount being charged, in major currency units.</summary>
    public decimal Price { get; set; }

    public long PriceInCents { get; set; }

    public string? Currency { get; set; }

    public int? Interval { get; set; }

    public string? IntervalUnit { get; set; }

    public string PriceDescription { get; set; } = string.Empty;

    /// <summary>When the next charge is scheduled.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? TrialEndedAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public bool CancelAtEndOfPeriod { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Outstanding balance in major currency units.</summary>
    public decimal Balance { get; set; }

    public string? PaymentCollectionMethod { get; set; }

    /// <summary>The reference eShopOnWeb stored on the subscription, if one was supplied.</summary>
    public string? Reference { get; set; }

    /// <summary>Billing-system customer that owns the subscription.</summary>
    public int? CustomerId { get; set; }

    /// <summary>The reference that links this billing customer back to the eShopOnWeb user.</summary>
    public string? CustomerReference { get; set; }
}
