using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's subscription, as reported by the billing system of record.
/// </summary>
public class SubscriptionDto
{
    /// <summary>Billing-provider identifier for the subscription.</summary>
    public long Id { get; set; }

    /// <summary>Deterministic reference this integration stamped on the subscription.</summary>
    public string? Reference { get; set; }

    /// <summary>Provider state, e.g. <c>active</c>, <c>trialing</c>, <c>past_due</c>, <c>canceled</c>.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>True while the subscription still entitles the shopper to the plan.</summary>
    public bool IsLive { get; set; }

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public long PriceInCents { get; set; }

    public string? Currency { get; set; }

    public int Interval { get; set; }

    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Outstanding balance on the subscription, as a decimal amount.</summary>
    public decimal Balance { get; set; }

    public long BalanceInCents { get; set; }

    /// <summary>How the provider collects payment, e.g. <c>remittance</c> or <c>automatic</c>.</summary>
    public string? PaymentCollectionMethod { get; set; }

    public long CustomerId { get; set; }

    public string? CustomerReference { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the subscription will next be billed.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? TrialEndedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }
}
