using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// An eShopOnWeb user's enrollment in a recurring <see cref="BillingPlan"/>.
/// </summary>
/// <remarks>
/// The billing provider is the system of record, so this aggregate is projected from the
/// provider on every read rather than persisted (see plan.md §8 — stateless mapping, idempotent
/// on the user reference). <see cref="BaseEntity.Id"/> therefore carries the provider's
/// subscription identifier.
/// <para>
/// The <c>Can*</c> members express which lifecycle transitions are legal from the current state.
/// They let UC4 reject an illegal transition without making a provider call, while the provider
/// remains the final authority when state has drifted out-of-band.
/// </para>
/// </remarks>
public class Subscription : BaseEntity, IAggregateRoot
{
    public Subscription(int id,
        string userReference,
        int customerId,
        BillingPlan plan,
        SubscriptionState state,
        string providerState)
    {
        Guard.Against.NegativeOrZero(id, nameof(id));
        Guard.Against.NullOrEmpty(userReference, nameof(userReference));
        Guard.Against.Null(plan, nameof(plan));
        Guard.Against.NullOrEmpty(providerState, nameof(providerState));

        Id = id;
        UserReference = userReference;
        CustomerId = customerId;
        Plan = plan;
        State = state;
        ProviderState = providerState;
    }

    /// <summary>The eShopOnWeb user this subscription belongs to (their username / email).</summary>
    public string UserReference { get; }

    /// <summary>The provider-side customer that owns this subscription.</summary>
    public int CustomerId { get; }

    /// <summary>The plan the subscription is currently on.</summary>
    public BillingPlan Plan { get; }

    /// <summary>The normalized lifecycle state.</summary>
    public SubscriptionState State { get; }

    /// <summary>The provider's own state name, preserved so unmapped states can still be shown.</summary>
    public string ProviderState { get; }

    public DateTimeOffset? ActivatedAt { get; init; }

    /// <summary>
    /// Start of the current billing period. This is the lower bound for the period-to-date usage
    /// total (UC2) — usage recorded before it belongs to an already-invoiced period.
    /// </summary>
    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    /// <summary>End of the current billing period — the boundary an end-of-period cancel defers to.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When the provider will next bill this subscription.</summary>
    public DateTimeOffset? NextAssessmentAt { get; init; }

    /// <summary>Set when an end-of-period cancellation is already pending.</summary>
    public DateTimeOffset? DelayedCancelAt { get; init; }

    /// <summary>True when the subscription is scheduled to cancel at the end of the current period.</summary>
    public bool CancelAtEndOfPeriod { get; init; }

    /// <summary>Outstanding balance in whole currency units (dollars).</summary>
    public decimal Balance { get; init; }

    /// <summary>The handle of a plan this subscription is scheduled to move to at next renewal, if any.</summary>
    public string? PendingPlanHandle { get; init; }

    /// <summary>True while the subscription is billing normally (or trialing).</summary>
    public bool IsActive => State is SubscriptionState.Active or SubscriptionState.Trialing;

    public bool CanPause => State is SubscriptionState.Active or SubscriptionState.Trialing;

    public bool CanResume => State is SubscriptionState.Paused;

    public bool CanCancel => State is not (SubscriptionState.Canceled or SubscriptionState.Expired);

    public bool CanReactivate => State is SubscriptionState.Canceled or SubscriptionState.Expired;

    public bool CanChangePlan =>
        State is SubscriptionState.Active or SubscriptionState.Trialing or SubscriptionState.PastDue;

    /// <summary>
    /// Usage may only be reported against a subscription the provider is actively billing.
    /// </summary>
    public bool CanRecordUsage => IsActive;
}
