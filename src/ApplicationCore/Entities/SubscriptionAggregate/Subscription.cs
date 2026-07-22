using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// An eShopOnWeb user's enrollment in a recurring plan, as reported by the billing provider.
/// </summary>
/// <remarks>
/// The billing provider is the system of record (there are no webhooks, so eShopOnWeb never
/// caches lifecycle state): every instance is projected from a live provider read. Money is
/// carried in whole currency units, never cents.
/// </remarks>
public sealed record Subscription
{
    /// <summary>The provider-assigned subscription identifier.</summary>
    public required int Id { get; init; }

    public required SubscriptionState State { get; init; }

    /// <summary>The provider-assigned customer identifier this subscription belongs to.</summary>
    public required int CustomerId { get; init; }

    /// <summary>The eShopOnWeb user reference (email/username) this subscription belongs to.</summary>
    public string? CustomerReference { get; init; }

    /// <summary>The durable handle of the plan currently being billed.</summary>
    public string? PlanHandle { get; init; }

    public string? PlanName { get; init; }

    /// <summary>The plan price in whole currency units.</summary>
    public decimal PlanPrice { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When the provider will next assess (bill) this subscription.</summary>
    public DateTimeOffset? NextAssessmentAt { get; init; }

    public DateTimeOffset? CanceledAt { get; init; }

    /// <summary>Set when a cancellation has been scheduled for the end of the period.</summary>
    public DateTimeOffset? DelayedCancelAt { get; init; }

    public bool CancelAtEndOfPeriod { get; init; }

    /// <summary>The handle of a plan change already scheduled for the next renewal, if any.</summary>
    public string? PendingPlanHandle { get; init; }

    /// <summary>The raw state string the provider reported, retained for diagnostics.</summary>
    public string? ProviderState { get; init; }

    /// <summary>True when the subscription is currently generating recurring charges.</summary>
    public bool IsActive => State is SubscriptionState.Active or SubscriptionState.Trialing;

    /// <summary>
    /// The lifecycle transitions that are legal from the current state. The provider remains the
    /// authority — this is a local pre-check so obviously invalid requests never reach it (UC4).
    /// </summary>
    public IReadOnlyList<SubscriptionLifecycleAction> LegalActions =>
        Enum.GetValues<SubscriptionLifecycleAction>().Where(IsActionLegal).ToArray();

    /// <summary>
    /// Returns true when <paramref name="action"/> is a legal transition from the current state.
    /// </summary>
    public bool IsActionLegal(SubscriptionLifecycleAction action) => action switch
    {
        SubscriptionLifecycleAction.Pause =>
            State is SubscriptionState.Active or SubscriptionState.Trialing,

        SubscriptionLifecycleAction.Resume =>
            State is SubscriptionState.Paused,

        SubscriptionLifecycleAction.Cancel =>
            State is SubscriptionState.Active or SubscriptionState.Trialing or SubscriptionState.PastDue
                or SubscriptionState.Paused or SubscriptionState.TrialEnded or SubscriptionState.Unpaid
                or SubscriptionState.Suspended,

        // Deferring to the period boundary only makes sense while a billing period is running.
        SubscriptionLifecycleAction.CancelAtEndOfPeriod =>
            State is SubscriptionState.Active or SubscriptionState.Trialing or SubscriptionState.PastDue,

        SubscriptionLifecycleAction.Reactivate =>
            State is SubscriptionState.Canceled or SubscriptionState.Expired
                or SubscriptionState.TrialEnded or SubscriptionState.Unpaid,

        _ => false
    };

    /// <summary>True when a plan change may be requested from the current state (UC3).</summary>
    public bool CanChangePlan =>
        State is SubscriptionState.Active or SubscriptionState.Trialing or SubscriptionState.PastDue;
}
