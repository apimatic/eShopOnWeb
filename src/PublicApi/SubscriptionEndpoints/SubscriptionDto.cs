using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's enrollment in a plan, as held by the billing system of record.
/// </summary>
public class SubscriptionDto
{
    /// <summary>Billing-system identifier for this subscription.</summary>
    public long Id { get; set; }

    /// <summary>
    /// Lifecycle state, e.g. "active", "trialing", "past_due", "canceled".
    /// </summary>
    public string State { get; set; } = string.Empty;

    /// <summary>True while the subscription still entitles the shopper to the plan.</summary>
    public bool IsActive { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    /// <summary>Recurring amount billed for this subscription, in <see cref="Currency"/>.</summary>
    public decimal Price { get; set; }

    public long PriceInCents { get; set; }

    public string? Currency { get; set; }

    public int Interval { get; set; }

    public string? IntervalUnit { get; set; }

    public string BillingPeriod { get; set; } = string.Empty;

    /// <summary>When the next charge will be attempted. Null once the subscription has ended.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? TrialEndsAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>How the subscription is collected: "automatic", "remittance", "prepaid" or "invoice".</summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>Outstanding balance on the subscription, in <see cref="Currency"/>.</summary>
    public decimal Balance { get; set; }

    /// <summary>The reference eShopOnWeb stored against this subscription in the billing system.</summary>
    public string? Reference { get; set; }

    /// <summary>Billing-system identifier of the customer that owns this subscription.</summary>
    public long CustomerId { get; set; }

    /// <summary>The reference eShopOnWeb stored against the billing customer.</summary>
    public string? CustomerReference { get; set; }
}
