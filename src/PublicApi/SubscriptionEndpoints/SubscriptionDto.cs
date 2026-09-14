using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's subscription, as held by the billing system of record.
/// </summary>
public class SubscriptionDto
{
    /// <summary>Billing provider id of the subscription.</summary>
    public long Id { get; set; }

    /// <summary>Reference eShopOnWeb assigned to the subscription when it was created.</summary>
    public string? Reference { get; set; }

    /// <summary>
    /// Lifecycle state reported by the billing provider: active, trialing, past_due, canceled, ...
    /// </summary>
    public string State { get; set; } = string.Empty;

    /// <summary>True when this subscription currently entitles the shopper to the plan.</summary>
    public bool IsLive { get; set; }

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    /// <summary>Recurring amount billed for this subscription, as a decimal amount.</summary>
    public decimal Price { get; set; }

    /// <summary>Recurring amount billed for this subscription, in minor units.</summary>
    public long PriceInCents { get; set; }

    public string Currency { get; set; } = string.Empty;

    public int Interval { get; set; }

    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Human readable billing period, e.g. "every month".</summary>
    public string BillingPeriod { get; set; } = string.Empty;

    public DateTimeOffset? CurrentPeriodStartsAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the next renewal charge is assessed.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>How renewals are collected: "automatic" (stored card) or "remittance" (invoiced).</summary>
    public string PaymentCollectionMethod { get; set; } = string.Empty;

    /// <summary>Outstanding balance on the subscription, in minor units.</summary>
    public long BalanceInCents { get; set; }

    /// <summary>Billing provider id of the customer this subscription belongs to.</summary>
    public long CustomerId { get; set; }

    /// <summary>Reference eShopOnWeb assigned to the billing customer.</summary>
    public string? CustomerReference { get; set; }
}
