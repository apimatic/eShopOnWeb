using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// An eShopOnWeb user's recurring subscription. The billing provider is the system of record,
/// so this aggregate is projected from the provider rather than persisted locally
/// (see plan.md §8 — the userId to customer mapping is stateless and idempotent on
/// <see cref="CustomerReference"/>).
/// </summary>
public class Subscription : BaseEntity, IAggregateRoot
{
    public Subscription(int id, string customerReference, int customerId, string planHandle,
        string planName, int planPriceInCents, SubscriptionState state, DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? activatedAt, bool cancelAtEndOfPeriod, DateTimeOffset? delayedCancelAt,
        DateTimeOffset? automaticallyResumeAt)
    {
        Id = id;
        CustomerReference = customerReference;
        CustomerId = customerId;
        PlanHandle = planHandle;
        PlanName = planName;
        PlanPriceInCents = planPriceInCents;
        State = state;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        ActivatedAt = activatedAt;
        CancelAtEndOfPeriod = cancelAtEndOfPeriod;
        DelayedCancelAt = delayedCancelAt;
        AutomaticallyResumeAt = automaticallyResumeAt;
    }

    /// <summary>
    /// The eShopOnWeb identity (username/email) this subscription belongs to.
    /// </summary>
    public string CustomerReference { get; }

    public int CustomerId { get; }
    public string PlanHandle { get; }
    public string PlanName { get; }

    /// <summary>
    /// The plan's recurring price in minor units (cents), as reported by the provider.
    /// </summary>
    public int PlanPriceInCents { get; }

    /// <summary>
    /// The plan's recurring price in major units (dollars).
    /// </summary>
    public decimal PlanPrice => PlanPriceInCents / 100m;

    public SubscriptionState State { get; }

    /// <summary>
    /// When the current billing period ends — the customer's next billing date.
    /// </summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; }

    public DateTimeOffset? ActivatedAt { get; }
    public bool CancelAtEndOfPeriod { get; }
    public DateTimeOffset? DelayedCancelAt { get; }
    public DateTimeOffset? AutomaticallyResumeAt { get; }

    /// <summary>
    /// True while the subscription is billing normally and can accept usage or a plan change.
    /// </summary>
    public bool IsLive => State is SubscriptionState.Active or SubscriptionState.Trialing
        or SubscriptionState.Assessing or SubscriptionState.Pending;
}
