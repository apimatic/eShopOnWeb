using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's subscription as reported by the billing system of record.
/// </summary>
public class CustomerSubscription
{
    /// <summary>Provider id of the subscription. Opaque to eShopOnWeb; safe to echo back to callers.</summary>
    public long Id { get; init; }

    /// <summary>The reference eShopOnWeb assigned to the subscription when it was created.</summary>
    public string? Reference { get; init; }

    /// <summary>Lifecycle state reported by the provider (active, trialing, past_due, canceled, ...).</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>True when the subscription entitles the shopper to the plan right now.</summary>
    public bool IsLive => SubscriptionStates.IsLive(State);

    public string PlanHandle { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;

    /// <summary>Recurring amount actually billed for this subscription, in the smallest currency unit.</summary>
    public long PriceInCents { get; init; }

    public decimal Price => PriceInCents / 100m;

    public string Currency { get; init; } = string.Empty;

    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;

    public DateTimeOffset? CurrentPeriodStartsAt { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>
    /// When the next renewal charge is assessed. Tracks the end of the current period except when a
    /// payment failed and is being retried, in which case it is the retry time.
    /// </summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }
    public DateTimeOffset? CanceledAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>How renewals are collected: "automatic" (stored card) or "remittance" (invoiced).</summary>
    public string PaymentCollectionMethod { get; init; } = string.Empty;

    /// <summary>Outstanding balance on the subscription, in the smallest currency unit.</summary>
    public long BalanceInCents { get; init; }

    /// <summary>Provider id of the customer the subscription belongs to.</summary>
    public long CustomerId { get; init; }

    /// <summary>The reference eShopOnWeb assigned to the customer.</summary>
    public string? CustomerReference { get; init; }
}
