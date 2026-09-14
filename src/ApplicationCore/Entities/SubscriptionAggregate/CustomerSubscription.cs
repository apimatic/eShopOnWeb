using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A shopper's subscription as reported by the billing provider.
/// </summary>
public class CustomerSubscription
{
    /// <summary>The billing provider's identifier for the subscription.</summary>
    public int Id { get; init; }

    /// <summary>The idempotency reference eShopOnWeb assigned to the subscription.</summary>
    public string? Reference { get; init; }

    /// <summary>Provider state, e.g. active, trialing, past_due, canceled.</summary>
    public required string State { get; init; }

    public string? PlanHandle { get; init; }
    public string? PlanName { get; init; }

    public long PriceInCents { get; init; }
    public decimal Price => PriceInCents / 100m;
    public string? Currency { get; init; }

    public int Interval { get; init; }
    public string? IntervalUnit { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When the subscription will next be assessed (billed).</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }
    public DateTimeOffset? CanceledAt { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }

    public int CustomerId { get; init; }
    public string? CustomerReference { get; init; }

    /// <summary>How the provider collects payment, e.g. automatic or remittance (invoice).</summary>
    public string? PaymentCollectionMethod { get; init; }

    /// <summary>
    /// True while the subscription still entitles the shopper to the plan. Mirrors the
    /// provider's "live" states; end-of-life states (canceled, expired, ...) are not live.
    /// </summary>
    public bool IsLive => SubscriptionStates.IsLive(State);
}
