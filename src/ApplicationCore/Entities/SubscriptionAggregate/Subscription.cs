using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A customer's enrollment in a recurring plan. The billing provider is the system of record;
/// this aggregate is the provider-agnostic view of it used throughout eShopOnWeb.
/// <see cref="BaseEntity.Id"/> carries the provider-side subscription identifier.
/// </summary>
public class Subscription : BaseEntity, IAggregateRoot
{
    public Subscription(int id,
        string userReference,
        int customerId,
        string planHandle,
        string planName,
        decimal planPrice,
        int interval,
        string intervalUnit,
        SubscriptionState state,
        DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? activatedAt,
        bool cancelAtEndOfPeriod,
        DateTimeOffset? delayedCancelAt)
    {
        Id = id;
        UserReference = userReference;
        CustomerId = customerId;
        PlanHandle = planHandle;
        PlanName = planName;
        PlanPrice = planPrice;
        Interval = interval;
        IntervalUnit = intervalUnit;
        State = state;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        ActivatedAt = activatedAt;
        CancelAtEndOfPeriod = cancelAtEndOfPeriod;
        DelayedCancelAt = delayedCancelAt;
    }

    /// <summary>The eShopOnWeb user this subscription belongs to (the customer reference, see §4.4).</summary>
    public string UserReference { get; private set; }

    /// <summary>The provider-side customer identifier.</summary>
    public int CustomerId { get; private set; }

    public string PlanHandle { get; private set; }
    public string PlanName { get; private set; }

    /// <summary>The recurring plan price, in whole currency units (e.g. 299.00), not minor units.</summary>
    public decimal PlanPrice { get; private set; }

    /// <summary>The numeric billing interval, e.g. 1 when coupled with an <see cref="IntervalUnit"/> of "month".</summary>
    public int Interval { get; private set; }

    public string IntervalUnit { get; private set; }

    public SubscriptionState State { get; private set; }

    /// <summary>When the current billing period ends — the next billing date for an active subscription.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; private set; }

    public DateTimeOffset? ActivatedAt { get; private set; }

    /// <summary>True when an end-of-period cancellation has been requested but not yet applied.</summary>
    public bool CancelAtEndOfPeriod { get; private set; }

    public DateTimeOffset? DelayedCancelAt { get; private set; }

    /// <summary>A subscription that is currently billing normally.</summary>
    public bool IsActive => State == SubscriptionState.Active || State == SubscriptionState.Trialing;

    /// <summary>A subscription whose billing has been temporarily stopped and which can be resumed.</summary>
    public bool IsPaused => State == SubscriptionState.OnHold;

    public bool IsCanceled => State == SubscriptionState.Canceled;
}
