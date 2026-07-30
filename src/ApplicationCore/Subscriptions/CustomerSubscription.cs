using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's enrollment in a plan, projected from a Maxio Advanced Billing subscription.
/// </summary>
public class CustomerSubscription
{
    public CustomerSubscription(long id, string state, string? planHandle, string? planName,
        long priceInCents, string? currency, int? interval, string? intervalUnit,
        DateTimeOffset? currentPeriodStartedAt, DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? nextBillingAt, DateTimeOffset? createdAt, string? paymentCollectionMethod)
    {
        Id = id;
        State = state;
        PlanHandle = planHandle;
        PlanName = planName;
        PriceInCents = priceInCents;
        Currency = currency;
        Interval = interval;
        IntervalUnit = intervalUnit;
        CurrentPeriodStartedAt = currentPeriodStartedAt;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextBillingAt = nextBillingAt;
        CreatedAt = createdAt;
        PaymentCollectionMethod = paymentCollectionMethod;
    }

    /// <summary>The Maxio subscription id.</summary>
    public long Id { get; }

    /// <summary>The subscription state (e.g. "active", "trialing", "past_due", "canceled").</summary>
    public string State { get; }

    public string? PlanHandle { get; }
    public string? PlanName { get; }

    public long PriceInCents { get; }
    public decimal Price => PriceInCents / 100m;
    public string? Currency { get; }

    public int? Interval { get; }
    public string? IntervalUnit { get; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; }

    /// <summary>When the next renewal charge will be assessed (the "next billing date").</summary>
    public DateTimeOffset? NextBillingAt { get; }

    public DateTimeOffset? CreatedAt { get; }

    public string? PaymentCollectionMethod { get; }

    /// <summary>
    /// Whether this subscription is in a live (revenue-bearing / access-granting) state as
    /// opposed to an end-of-life state such as canceled or expired.
    /// </summary>
    public bool IsLive => State is "active" or "trialing" or "assessing" or "pending"
        or "past_due" or "soft_failure" or "paused" or "awaiting_signup";
}
