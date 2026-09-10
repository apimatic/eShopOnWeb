using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper-facing view of an enrolled subscription: which plan, at what price,
/// its current state and when it next bills.
/// </summary>
public record SubscriptionSummary
{
    /// <summary>Provider-assigned subscription id.</summary>
    public long Id { get; init; }

    /// <summary>Lifecycle state as reported by the billing provider (e.g. "active", "trialing", "canceled").</summary>
    public required string State { get; init; }

    public required string PlanHandle { get; init; }

    public required string PlanName { get; init; }

    /// <summary>Recurring price in the major currency unit.</summary>
    public decimal Price { get; init; }

    public required string IntervalUnit { get; init; }

    public int IntervalCount { get; init; }

    /// <summary>When the subscription next bills (provider's current period end / next assessment). Null if not applicable.</summary>
    public DateTimeOffset? NextBillingDate { get; init; }

    /// <summary>How the subscription is collected, e.g. "remittance" (invoice) or "automatic" (card on file).</summary>
    public required string PaymentCollectionMethod { get; init; }
}
