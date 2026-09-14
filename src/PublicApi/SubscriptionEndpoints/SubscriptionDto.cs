using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's subscription as returned to API callers.
/// </summary>
public class SubscriptionDto
{
    /// <summary>Identifier of the subscription in the billing system.</summary>
    public int Id { get; set; }

    /// <summary>Reference eShopOnWeb assigned to this subscription at signup.</summary>
    public string? Reference { get; set; }

    /// <summary>Billing state, e.g. active, trialing, past_due or canceled.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>Whether the subscription currently entitles the shopper to the plan.</summary>
    public bool IsActive { get; set; }

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    public long PriceInCents { get; set; }

    public decimal Price { get; set; }

    public string Currency { get; set; } = string.Empty;

    public int Interval { get; set; }

    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Human readable billing period, e.g. "month".</summary>
    public string BillingPeriod { get; set; } = string.Empty;

    /// <summary>When the next renewal charge is scheduled.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Outstanding balance in the smallest unit of <see cref="Currency"/>.</summary>
    public long BalanceInCents { get; set; }

    public decimal Balance { get; set; }

    /// <summary>How the billing system collects payment for this subscription.</summary>
    public string? PaymentCollectionMethod { get; set; }
}
