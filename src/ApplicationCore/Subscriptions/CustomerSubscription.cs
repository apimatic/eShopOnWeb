using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's enrollment in a plan, as reported by Maxio (the billing system of record).
/// </summary>
public sealed record CustomerSubscription
{
    public CustomerSubscription(int id, string state, string planHandle, string planName,
        long pricePerPeriodInCents, int interval, string intervalUnit, string currencyCode,
        DateTimeOffset? currentPeriodEndsAt, DateTimeOffset? nextBillingAt, DateTimeOffset? createdAt)
    {
        Id = id;
        State = state;
        PlanHandle = planHandle;
        PlanName = planName;
        PricePerPeriodInCents = pricePerPeriodInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
        CurrencyCode = currencyCode;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextBillingAt = nextBillingAt;
        CreatedAt = createdAt;
    }

    /// <summary>The Maxio subscription id.</summary>
    public int Id { get; }

    /// <summary>The Maxio subscription state (e.g. "active", "trialing", "canceled").</summary>
    public string State { get; }

    /// <summary>Handle of the plan (Maxio product) the shopper is subscribed to.</summary>
    public string PlanHandle { get; }

    /// <summary>Name of the plan the shopper is subscribed to.</summary>
    public string PlanName { get; }

    /// <summary>The recurring amount charged per period, in integer cents.</summary>
    public long PricePerPeriodInCents { get; }

    /// <summary>The numeric billing interval (e.g. 1).</summary>
    public int Interval { get; }

    /// <summary>The billing interval unit (e.g. "month").</summary>
    public string IntervalUnit { get; }

    /// <summary>ISO currency code the price is expressed in (e.g. "USD").</summary>
    public string CurrencyCode { get; }

    /// <summary>End of the current billing period.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; }

    /// <summary>
    /// When the next renewal charge will be attempted. This is the value surfaced to shoppers as
    /// their "next billing date".
    /// </summary>
    public DateTimeOffset? NextBillingAt { get; }

    /// <summary>When the subscription was created.</summary>
    public DateTimeOffset? CreatedAt { get; }

    /// <summary>
    /// Whether the subscription is in a state that should be treated as an existing enrollment for
    /// idempotency purposes (i.e. anything that is not an end-of-life state).
    /// </summary>
    public bool IsLive =>
        !string.Equals(State, "canceled", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(State, "expired", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(State, "failed_to_create", StringComparison.OrdinalIgnoreCase);
}
