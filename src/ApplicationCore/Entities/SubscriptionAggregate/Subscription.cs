using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Links an eShopOnWeb user to a recurring subscription held by the billing provider.
/// The provider is the system of record; this aggregate is eShopOnWeb's view of it.
/// </summary>
public class Subscription : BaseEntity, IAggregateRoot
{
    public Subscription(int providerSubscriptionId, int providerCustomerId, string buyerId,
        SubscriptionPlan plan, SubscriptionState state, DateTimeOffset? currentPeriodEndsAt,
        bool cancelAtEndOfPeriod, DateTimeOffset? canceledAt, DateTimeOffset? automaticallyResumeAt)
    {
        Id = providerSubscriptionId;
        ProviderCustomerId = providerCustomerId;
        BuyerId = buyerId;
        Plan = plan;
        State = state;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        CancelAtEndOfPeriod = cancelAtEndOfPeriod;
        CanceledAt = canceledAt;
        AutomaticallyResumeAt = automaticallyResumeAt;
    }

    /// <summary><see cref="BaseEntity.Id"/> carries the provider's subscription identifier.</summary>
    public int ProviderSubscriptionId => Id;

    public int ProviderCustomerId { get; private set; }

    /// <summary>The eShopOnWeb user reference (email / username) this subscription belongs to.</summary>
    public string BuyerId { get; private set; }

    public SubscriptionPlan Plan { get; private set; }

    public SubscriptionState State { get; private set; }

    /// <summary>When the current billing period ends — the customer's next billing date.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; private set; }

    /// <summary>True when an end-of-period cancellation has been requested but not yet applied.</summary>
    public bool CancelAtEndOfPeriod { get; private set; }

    public DateTimeOffset? CanceledAt { get; private set; }

    /// <summary>When a paused subscription is scheduled to resume automatically, if any.</summary>
    public DateTimeOffset? AutomaticallyResumeAt { get; private set; }

    /// <summary>
    /// A subscription is billable — and therefore able to accrue metered usage — while it is
    /// active or in trial.
    /// </summary>
    public bool IsActive => State == SubscriptionState.Active || State == SubscriptionState.Trialing;
}
