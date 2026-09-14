using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's subscription as it exists in the billing provider, which is the system of record.
/// </summary>
public class CustomerSubscription
{
    /// <summary>The billing provider's identifier for the subscription.</summary>
    public required string Id { get; init; }

    /// <summary>Raw provider state, e.g. "active", "trialing", "past_due", "canceled".</summary>
    public required string State { get; init; }

    /// <summary>The idempotency reference eShopOnWeb supplied when the subscription was created.</summary>
    public string? Reference { get; init; }

    /// <summary>
    /// Handle of the plan the shopper is enrolled on. Null only for subscriptions that were not built
    /// from a plan, which this integration never creates but may read back from a shared billing site.
    /// </summary>
    public string? PlanHandle { get; init; }

    public string? PlanName { get; init; }

    /// <summary>Recurring price the shopper is actually being charged, in minor units.</summary>
    public required long PriceInCents { get; init; }

    public required string Currency { get; init; }

    public BillingInterval? Interval { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When the provider will next attempt to bill this subscription.</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CanceledAt { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Outstanding balance on the subscription, in minor units.</summary>
    public long BalanceInCents { get; init; }

    /// <summary>How the provider collects payment, e.g. "remittance" (invoice) or "automatic" (card on file).</summary>
    public string? PaymentCollectionMethod { get; init; }

    /// <summary>The billing provider's identifier for the customer that owns the subscription.</summary>
    public required string CustomerId { get; init; }

    public bool IsLive => SubscriptionStates.IsLive(State);

    public bool IsHealthy => SubscriptionStates.IsHealthy(State);
}
