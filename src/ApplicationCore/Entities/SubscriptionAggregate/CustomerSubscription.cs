using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A shopper's enrollment in a plan, as reported by the billing system of record.
/// </summary>
public sealed class CustomerSubscription
{
    public long Id { get; init; }

    /// <summary>The billing-system subscription state (e.g. <c>active</c>, <c>trialing</c>, <c>canceled</c>).</summary>
    public required string State { get; init; }

    public required string PlanHandle { get; init; }

    public required string PlanName { get; init; }

    /// <summary>The recurring price of the subscribed plan, in integer cents.</summary>
    public long PriceInCents { get; init; }

    public string? Currency { get; init; }

    /// <summary>End of the current billing period.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When the next billing attempt is scheduled (the next billing date shown to the user).</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    public decimal Price => PriceInCents / 100m;

    /// <summary>Whether the subscription is currently providing service (not end-of-life).</summary>
    public bool IsLive => SubscriptionState.IsLive(State);
}
