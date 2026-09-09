using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription belonging to a customer, in provider-agnostic terms.
/// </summary>
public class CustomerSubscription
{
    public CustomerSubscription(
        long id,
        string state,
        string planHandle,
        string planName,
        int priceInCents,
        string currency,
        int interval,
        string intervalUnit,
        DateTimeOffset? currentPeriodStartedAt,
        DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? nextBillingDate)
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
        NextBillingDate = nextBillingDate;
    }

    /// <summary>Billing-system subscription id.</summary>
    public long Id { get; }

    /// <summary>Lifecycle state, e.g. "active", "trialing", "canceled".</summary>
    public string State { get; }

    public string PlanHandle { get; }

    public string PlanName { get; }

    public int PriceInCents { get; }

    public string Currency { get; }

    public int Interval { get; }

    public string IntervalUnit { get; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; }

    /// <summary>When the next charge/assessment is scheduled.</summary>
    public DateTimeOffset? NextBillingDate { get; }

    /// <summary>
    /// True when the subscription is in a live, revenue-generating state and should be
    /// treated as an existing enrolment for idempotency purposes.
    /// </summary>
    public bool IsLive =>
        State is "active" or "trialing" or "assessing" or "pending" or "soft_failure" or "past_due";
}
