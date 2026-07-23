using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A customer's recurring subscription. The billing provider is the system of record, so this
/// aggregate is projected from the provider on every read rather than persisted locally
/// (the eShopOnWeb user is linked to the provider customer through <see cref="CustomerReference"/>).
/// </summary>
public class Subscription : BaseEntity, IAggregateRoot
{
    public Subscription(int id,
        int customerId,
        string? customerReference,
        SubscriptionPlan plan,
        SubscriptionState state,
        string providerState,
        DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? nextAssessmentAt,
        bool cancelAtEndOfPeriod,
        DateTimeOffset? delayedCancelAt,
        int balanceInCents,
        string? pendingPlanHandle = null)
    {
        Guard.Against.Null(plan, nameof(plan));
        Guard.Against.NullOrEmpty(providerState, nameof(providerState));

        Id = id;
        CustomerId = customerId;
        CustomerReference = customerReference;
        Plan = plan;
        State = state;
        ProviderState = providerState;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextAssessmentAt = nextAssessmentAt;
        CancelAtEndOfPeriod = cancelAtEndOfPeriod;
        DelayedCancelAt = delayedCancelAt;
        BalanceInCents = balanceInCents;
        PendingPlanHandle = pendingPlanHandle;
    }

    /// <summary>The billing provider's customer id that owns this subscription.</summary>
    public int CustomerId { get; }

    /// <summary>The eShopOnWeb user reference (username/email) the provider customer was created with.</summary>
    public string? CustomerReference { get; }

    public SubscriptionPlan Plan { get; }

    public SubscriptionState State { get; }

    /// <summary>The verbatim state reported by the provider, e.g. "trialing" or "soft_failure".</summary>
    public string ProviderState { get; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; }

    /// <summary>When the next invoice will be assessed. This is the customer-facing "next billing date".</summary>
    public DateTimeOffset? NextAssessmentAt { get; }

    public bool CancelAtEndOfPeriod { get; }

    public DateTimeOffset? DelayedCancelAt { get; }

    /// <summary>
    /// The handle of a plan this subscription is scheduled to move to at the next renewal, or null
    /// when no delayed plan change is pending.
    /// </summary>
    public string? PendingPlanHandle { get; }

    /// <summary>The outstanding balance in minor units.</summary>
    public int BalanceInCents { get; }

    /// <summary>The outstanding balance in major units.</summary>
    public decimal Balance => BalanceInCents / 100m;

    /// <summary>
    /// True while the subscription still occupies the customer's single active enrollment slot, so a
    /// repeated subscribe returns this subscription instead of creating a second one.
    /// </summary>
    public bool IsLive => State is SubscriptionState.Pending
        or SubscriptionState.Active
        or SubscriptionState.PastDue;
}
