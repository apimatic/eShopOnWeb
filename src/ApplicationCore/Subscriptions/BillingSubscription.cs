using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription held by a customer in the billing system.
/// </summary>
public class BillingSubscription
{
    public int Id { get; init; }

    /// <summary>The Maxio subscription state, e.g. "active", "trialing", "canceled".</summary>
    public string State { get; init; } = string.Empty;

    public int CustomerId { get; init; }
    public string? CustomerReference { get; init; }

    public string? PlanHandle { get; init; }
    public string? PlanName { get; init; }
    public long PlanPriceInCents { get; init; }
    public int PlanInterval { get; init; }
    public string? PlanIntervalUnit { get; init; }

    /// <summary>The price actually being charged for this subscription, which can differ from the
    /// plan's current price when the plan was re-priced after signup.</summary>
    public long PriceInCents { get; init; }

    /// <summary>The billing site's currency, e.g. "USD".</summary>
    public string Currency { get; init; } = string.Empty;

    /// <summary>When the next renewal charge will be attempted.</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? CurrentPeriodStartsAt { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset? ActivatedAt { get; init; }
    public DateTimeOffset? CanceledAt { get; init; }
    public DateTimeOffset? TrialEndedAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public long BalanceInCents { get; init; }

    /// <summary>True while the subscription still entitles the shopper to the plan.</summary>
    public bool IsLive => SubscriptionStates.IsLive(State);
}
