using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's subscription, as held by the billing system of record.
/// </summary>
public class CustomerSubscription
{
    /// <summary>Identifier assigned by the billing system.</summary>
    public long Id { get; init; }

    /// <summary>The reference this application assigned to the subscription; also its idempotency key.</summary>
    public string? Reference { get; init; }

    /// <summary>Lifecycle state reported by the billing system, for example "active" or "canceled".</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>True while the subscription still entitles the shopper to the plan.</summary>
    public bool IsLive => SubscriptionStates.IsLive(State);

    public string PlanHandle { get; init; } = string.Empty;

    public string PlanName { get; init; } = string.Empty;

    public long PriceInCents { get; init; }

    public decimal Price => PriceInCents / 100m;

    public string Currency { get; init; } = "USD";

    public int Interval { get; init; }

    public string IntervalUnit { get; init; } = string.Empty;

    public DateTimeOffset? CurrentPeriodStartsAt { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When the billing system will next assess (bill) this subscription.</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CanceledAt { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public DateTimeOffset? TrialEndsAt { get; init; }

    /// <summary>How the recurring charge is collected, for example "automatic" or "remittance".</summary>
    public string? PaymentCollectionMethod { get; init; }

    /// <summary>Outstanding balance on the subscription, in the smallest currency unit.</summary>
    public long BalanceInCents { get; init; }

    /// <summary>Billing-system identifier of the customer that owns the subscription.</summary>
    public long CustomerId { get; init; }

    /// <summary>The reference this application assigned to the billing customer.</summary>
    public string? CustomerReference { get; init; }
}
