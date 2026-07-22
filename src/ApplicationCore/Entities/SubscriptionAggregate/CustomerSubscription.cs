using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// An eShopOnWeb customer's subscription, as reported by the billing provider.
/// <para>
/// This is a read model over provider state, not a persisted aggregate: the provider is the system
/// of record (see plan.md §8 — the eShopOnWeb ↔ provider mapping is stateless and idempotent on
/// <see cref="CustomerReference"/>). Money is in whole currency units, never cents.
/// </para>
/// </summary>
public class CustomerSubscription
{
    /// <summary>States a subscription is considered "live" in — a customer may hold only one at a time.</summary>
    private static readonly SubscriptionState[] LiveStates =
    {
        SubscriptionState.Active,
        SubscriptionState.Trialing
    };

    /// <summary>States the provider accepts a reactivation from.</summary>
    private static readonly SubscriptionState[] ReactivatableStates =
    {
        SubscriptionState.Canceled,
        SubscriptionState.Unpaid,
        SubscriptionState.TrialEnded
    };

    /// <summary>States a cancellation can no longer be requested from.</summary>
    private static readonly SubscriptionState[] TerminalStates =
    {
        SubscriptionState.Canceled,
        SubscriptionState.Expired,
        SubscriptionState.FailedToCreate
    };

    public CustomerSubscription(int id,
        SubscriptionState state,
        string customerReference,
        int customerId)
    {
        Guard.Against.NullOrEmpty(customerReference, nameof(customerReference));

        Id = id;
        State = state;
        CustomerReference = customerReference;
        CustomerId = customerId;
    }

    public int Id { get; }

    public SubscriptionState State { get; }

    /// <summary>The eShopOnWeb user reference (email/username) that owns this subscription.</summary>
    public string CustomerReference { get; }

    public int CustomerId { get; }

    public string? CustomerEmail { get; init; }

    public int? PlanId { get; init; }

    public string? PlanHandle { get; init; }

    public string? PlanName { get; init; }

    /// <summary>The recurring plan price in whole currency units (dollars).</summary>
    public decimal PlanPrice { get; init; }

    public string? Currency { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When the next invoice will be assessed — the "next billing date" shown to the customer.</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CanceledAt { get; init; }

    /// <summary>True when an end-of-period cancellation has been requested but not yet applied.</summary>
    public bool CancelAtEndOfPeriod { get; init; }

    public DateTimeOffset? ScheduledCancellationAt { get; init; }

    public DateTimeOffset? OnHoldAt { get; init; }

    public DateTimeOffset? AutomaticallyResumeAt { get; init; }

    /// <summary>Set when a plan change has been scheduled for the next renewal but has not taken effect yet.</summary>
    public int? PendingPlanId { get; init; }

    public string? PendingPlanHandle { get; init; }

    /// <summary>True when this subscription occupies the customer's "one live subscription" slot.</summary>
    public bool IsLive => LiveStates.Contains(State);

    /// <summary>True when a plan change is scheduled to take effect at the next renewal.</summary>
    public bool HasPendingPlanChange => PendingPlanId.HasValue || !string.IsNullOrEmpty(PendingPlanHandle);

    /// <summary>
    /// The lifecycle actions that are legal from the current state. Used to reject an illegal
    /// transition locally, before any provider call is made (UC4).
    /// </summary>
    public IReadOnlyCollection<SubscriptionLifecycleAction> AllowedActions
    {
        get
        {
            var allowed = new List<SubscriptionLifecycleAction>();

            if (State == SubscriptionState.Active)
            {
                allowed.Add(SubscriptionLifecycleAction.Pause);
            }

            if (State is SubscriptionState.OnHold or SubscriptionState.Paused)
            {
                allowed.Add(SubscriptionLifecycleAction.Resume);
            }

            if (!TerminalStates.Contains(State) && State != SubscriptionState.Unknown)
            {
                allowed.Add(SubscriptionLifecycleAction.Cancel);
            }

            if (ReactivatableStates.Contains(State))
            {
                allowed.Add(SubscriptionLifecycleAction.Reactivate);
            }

            return allowed;
        }
    }

    /// <summary>
    /// Whether the given lifecycle action is legal from the current state. <paramref name="timing"/>
    /// matters only for <see cref="SubscriptionLifecycleAction.Cancel"/>: the provider refuses an
    /// end-of-period cancellation while a subscription is past due.
    /// </summary>
    public bool CanApply(SubscriptionLifecycleAction action, CancellationTiming timing = CancellationTiming.Immediate)
    {
        if (!AllowedActions.Contains(action))
        {
            return false;
        }

        if (action == SubscriptionLifecycleAction.Cancel && timing == CancellationTiming.EndOfPeriod)
        {
            // The provider rejects a delayed cancellation while the subscription is past due, and a
            // second delayed cancellation when one is already pending.
            return State != SubscriptionState.PastDue && !CancelAtEndOfPeriod;
        }

        return true;
    }
}
