using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A customer's recurring subscription. The billing provider is the system of record, so this
/// aggregate is a normalized projection of the provider's view keyed by the provider's id.
/// </summary>
public class Subscription : BaseEntity, IAggregateRoot
{
    public Subscription(int id,
        int customerId,
        string customerReference,
        string planHandle,
        string planName,
        long planPriceInCents,
        SubscriptionState state,
        DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? nextAssessmentAt,
        bool cancelAtEndOfPeriod,
        DateTimeOffset? delayedCancelAt)
    {
        Guard.Against.NullOrWhiteSpace(customerReference, nameof(customerReference));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        Guard.Against.NullOrWhiteSpace(planName, nameof(planName));

        Id = id;
        CustomerId = customerId;
        CustomerReference = customerReference;
        PlanHandle = planHandle;
        PlanName = planName;
        PlanPriceInCents = planPriceInCents;
        State = state;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextAssessmentAt = nextAssessmentAt;
        CancelAtEndOfPeriod = cancelAtEndOfPeriod;
        DelayedCancelAt = delayedCancelAt;
    }

    /// <summary>The provider-assigned customer id that owns this subscription.</summary>
    public int CustomerId { get; }

    /// <summary>
    /// The stable eShopOnWeb reference (the signed-in user's email/username) this subscription
    /// belongs to. This is what makes subscribe idempotent per user.
    /// </summary>
    public string CustomerReference { get; }

    public string PlanHandle { get; }

    public string PlanName { get; }

    /// <summary>The plan's recurring price in minor currency units (cents).</summary>
    public long PlanPriceInCents { get; }

    /// <summary>The plan's recurring price in major currency units.</summary>
    public decimal PlanPrice => PlanPriceInCents / 100m;

    public SubscriptionState State { get; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; }

    /// <summary>When the provider will next bill this subscription.</summary>
    public DateTimeOffset? NextAssessmentAt { get; }

    public bool CancelAtEndOfPeriod { get; }

    public DateTimeOffset? DelayedCancelAt { get; }

    /// <summary>
    /// True when the subscription is in a state that can accrue metered usage and be managed.
    /// </summary>
    public bool IsActive => State is SubscriptionState.Active or SubscriptionState.Trialing or SubscriptionState.Assessing;
}
