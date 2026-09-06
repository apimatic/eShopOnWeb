using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;

/// <summary>
/// A shopper's enrollment in a <see cref="SubscriptionPlan"/>, as reported by the billing
/// system of record.
/// </summary>
public class CustomerSubscription
{
    /// <summary>Identifier of the subscription in the billing system.</summary>
    public long Id { get; init; }

    /// <summary>Lifecycle state, e.g. "active", "trialing", "past_due", "canceled".</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>True while the subscription still entitles the shopper to the plan.</summary>
    public bool IsLive { get; init; }

    public string PlanHandle { get; init; } = string.Empty;

    public string PlanName { get; init; } = string.Empty;

    public long PriceInCents { get; init; }

    public string Currency { get; init; } = string.Empty;

    public int Interval { get; init; }

    public string IntervalUnit { get; init; } = string.Empty;

    /// <summary>When the next renewal charge is scheduled. Null for subscriptions that will not renew.</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CanceledAt { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Outstanding balance in the smallest unit of <see cref="Currency"/>.</summary>
    public long BalanceInCents { get; init; }

    /// <summary>How the recurring charge is collected, e.g. "automatic" or "remittance".</summary>
    public string PaymentCollectionMethod { get; init; } = string.Empty;

    /// <summary>Identifier of the owning customer in the billing system.</summary>
    public long CustomerId { get; init; }

    /// <summary>The eShopOnWeb-owned key that links the billing customer back to the local user.</summary>
    public string? CustomerReference { get; init; }

    public decimal Price => PriceInCents / 100m;
}
