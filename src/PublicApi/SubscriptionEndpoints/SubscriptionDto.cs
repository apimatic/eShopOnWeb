using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's enrollment in a subscription plan.
/// </summary>
public class SubscriptionDto
{
    /// <summary>Billing-system identifier of the subscription.</summary>
    public int Id { get; set; }

    /// <summary>Reference assigned by eShopOnWeb at signup.</summary>
    public string? Reference { get; set; }

    /// <summary>Billing-system state, e.g. <c>active</c>, <c>trialing</c>, <c>past_due</c>, <c>canceled</c>.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>True while the subscription still entitles the shopper to the plan.</summary>
    public bool IsLive { get; set; }

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    /// <summary>Recurring price expressed in <see cref="Currency"/>.</summary>
    public decimal Price { get; set; }

    public long PriceInCents { get; set; }

    /// <summary>ISO-4217 currency code.</summary>
    public string Currency { get; set; } = string.Empty;

    public int? Interval { get; set; }

    public string? IntervalUnit { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the next charge will be assessed.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? TrialEndedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Outstanding balance in the smallest unit of <see cref="Currency"/>.</summary>
    public long BalanceInCents { get; set; }

    /// <summary>How the billing system collects payment for this subscription.</summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>Billing-system identifier of the customer that owns the subscription.</summary>
    public int CustomerId { get; set; }

    /// <summary>The eShopOnWeb-owned reference that links the billing customer to the shopper.</summary>
    public string? CustomerReference { get; set; }
}
