using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's subscription as recorded in Maxio (the billing system of record). Projected from a
/// Maxio subscription resource. eShopOnWeb keeps no local copy of billing state — this is read back
/// from Maxio so the account view always reflects the source of truth.
/// </summary>
public class CustomerSubscription
{
    public CustomerSubscription(long id, string state, string planHandle, string planName,
        long priceInCents, string currency, string intervalUnit, DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? nextAssessmentAt, DateTimeOffset? createdAt, string? reference)
    {
        Id = id;
        State = state;
        PlanHandle = planHandle;
        PlanName = planName;
        PriceInCents = priceInCents;
        Currency = currency;
        IntervalUnit = intervalUnit;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextAssessmentAt = nextAssessmentAt;
        CreatedAt = createdAt;
        Reference = reference;
    }

    /// <summary>Maxio subscription id.</summary>
    public long Id { get; }

    /// <summary>Maxio lifecycle state (e.g. "active", "trialing", "canceled").</summary>
    public string State { get; }

    public string PlanHandle { get; }

    public string PlanName { get; }

    public long PriceInCents { get; }

    public string Currency { get; }

    public string IntervalUnit { get; }

    /// <summary>End of the current billing period — i.e. the next billing date.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; }

    /// <summary>When Maxio will next assess/charge the subscription.</summary>
    public DateTimeOffset? NextAssessmentAt { get; }

    public DateTimeOffset? CreatedAt { get; }

    /// <summary>The reference eShopOnWeb assigned to this subscription, when set.</summary>
    public string? Reference { get; }

    /// <summary>
    /// Whether the subscription is in a state that should block a duplicate subscribe to the same plan.
    /// Canceled / expired / failed subscriptions do not count, so a shopper can re-subscribe after churn.
    /// </summary>
    public bool IsLive => !SubscriptionStates.Inactive.Contains(State);
}
