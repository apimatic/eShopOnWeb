using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A customer's enrolment in a recurring plan. The billing provider is the system of record;
/// this aggregate is eShopOnWeb's view of it (plan.md §8 — the userId ↔ subscription mapping is
/// stateless and idempotent on the customer reference, so this type is not persisted).
/// </summary>
public class Subscription : BaseEntity, IAggregateRoot
{
    public Subscription(int id,
        int customerId,
        string customerReference,
        SubscriptionState state,
        string planHandle,
        string planName,
        long planPriceInCents,
        DateTimeOffset? currentPeriodStartedAt,
        DateTimeOffset? currentPeriodEndsAt,
        bool cancelAtEndOfPeriod,
        DateTimeOffset? canceledAt,
        string? nextPlanHandle)
    {
        Guard.Against.NullOrEmpty(customerReference, nameof(customerReference));
        Guard.Against.NullOrEmpty(planHandle, nameof(planHandle));

        Id = id;
        CustomerId = customerId;
        CustomerReference = customerReference;
        State = state;
        PlanHandle = planHandle;
        PlanName = planName;
        PlanPriceInCents = planPriceInCents;
        CurrentPeriodStartedAt = currentPeriodStartedAt;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        CancelAtEndOfPeriod = cancelAtEndOfPeriod;
        CanceledAt = canceledAt;
        NextPlanHandle = nextPlanHandle;
    }

    /// <summary>The provider's customer id this subscription belongs to.</summary>
    public int CustomerId { get; private set; }

    /// <summary>The eShopOnWeb user this subscription belongs to (email / username).</summary>
    public string CustomerReference { get; private set; }

    public SubscriptionState State { get; private set; }

    public string PlanHandle { get; private set; }
    public string PlanName { get; private set; }

    /// <summary>The plan's recurring price in minor units (cents), as charged by the provider.</summary>
    public long PlanPriceInCents { get; private set; }

    /// <summary>The plan's recurring price in major units (dollars).</summary>
    public decimal PlanPrice => PlanPriceInCents / 100m;

    public DateTimeOffset? CurrentPeriodStartedAt { get; private set; }

    /// <summary>The next billing date shown to the customer on confirmation (UC1 step 7).</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; private set; }

    /// <summary>True once an end-of-period cancellation has been scheduled (UC4).</summary>
    public bool CancelAtEndOfPeriod { get; private set; }

    public DateTimeOffset? CanceledAt { get; private set; }

    /// <summary>Set when a plan change has been scheduled for the next renewal (UC3).</summary>
    public string? NextPlanHandle { get; private set; }

    /// <summary>
    /// Whether the subscription is currently earning revenue — the precondition for recording
    /// usage (UC2) and for changing plan (UC3).
    /// </summary>
    public bool IsLive => State is SubscriptionState.Active
        or SubscriptionState.Trialing
        or SubscriptionState.Assessing
        or SubscriptionState.Pending;

    /// <summary>The lifecycle actions (UC4) that are legal from the current state.</summary>
    public IReadOnlyCollection<string> LegalActions
    {
        get
        {
            var actions = new List<string>();
            if (CanPause) actions.Add(SubscriptionActions.Pause);
            if (CanResume) actions.Add(SubscriptionActions.Resume);
            if (CanCancel) actions.Add(SubscriptionActions.Cancel);
            if (CanReactivate) actions.Add(SubscriptionActions.Reactivate);
            if (CanChangePlan) actions.Add(SubscriptionActions.ChangePlan);
            return actions;
        }
    }

    /// <summary>A subscription must be live to be put on hold; an already-held one cannot be paused again.</summary>
    public bool CanPause => IsLive;

    /// <summary>Only a held subscription can be resumed.</summary>
    public bool CanResume => State == SubscriptionState.OnHold;

    /// <summary>Anything that has not already reached end-of-life can be cancelled.</summary>
    public bool CanCancel => State is not (SubscriptionState.Canceled
        or SubscriptionState.Expired
        or SubscriptionState.FailedToCreate
        or SubscriptionState.Unknown);

    /// <summary>Only an end-of-life subscription can be reactivated.</summary>
    public bool CanReactivate => State is SubscriptionState.Canceled
        or SubscriptionState.Expired
        or SubscriptionState.TrialEnded;

    /// <summary>The provider only migrates subscriptions that are active or trialing.</summary>
    public bool CanChangePlan => State is SubscriptionState.Active or SubscriptionState.Trialing;

    /// <summary>Throws when <paramref name="action"/> is not legal from the current state (UC4).</summary>
    public void EnsureCanTransition(string action, bool isLegal)
    {
        if (!isLegal)
        {
            throw new Exceptions.IllegalSubscriptionTransitionException(
                State.ToString(), action, LegalActions.DefaultIfEmpty("none").ToList());
        }
    }
}
