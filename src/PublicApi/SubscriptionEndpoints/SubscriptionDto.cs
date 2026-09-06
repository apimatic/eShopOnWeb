using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A shopper's enrollment in a plan, as reported by the billing system of record.</summary>
public class SubscriptionDto
{
    /// <summary>Identifier of the subscription in the billing system.</summary>
    public long Id { get; set; }

    /// <summary>Lifecycle state, e.g. "active", "trialing", "past_due", "canceled".</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>True while this subscription still entitles the shopper to the plan.</summary>
    public bool IsLive { get; set; }

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public long PriceInCents { get; set; }

    public string Currency { get; set; } = string.Empty;

    public int? IntervalLength { get; set; }

    public string? IntervalUnit { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the plan renews next. Null once the subscription has reached end of life.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    /// <summary>Amount currently owed, in major currency units.</summary>
    public decimal Balance { get; set; }

    /// <summary>How the balance is collected: "remittance" (invoice) or "automatic" (card on file).</summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>Identifier of the shopper's customer record in the billing system.</summary>
    public long BillingCustomerId { get; set; }
}
