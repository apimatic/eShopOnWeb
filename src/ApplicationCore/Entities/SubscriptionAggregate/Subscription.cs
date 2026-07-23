using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// An eShopOnWeb user's subscription, as reported by the billing provider.
/// </summary>
/// <remarks>
/// The billing provider is the system of record for subscriptions, and the mapping between an
/// eShopOnWeb user and their subscriptions is stateless — it is resolved on demand through the
/// provider-side customer <see cref="CustomerReference"/>, which is the user's email / username.
/// Instances are therefore point-in-time snapshots rather than persisted aggregates.
/// </remarks>
/// <param name="Id">The provider-assigned subscription identifier.</param>
/// <param name="State">The normalized lifecycle state.</param>
/// <param name="CustomerId">The provider-assigned customer identifier that owns this subscription.</param>
/// <param name="CustomerReference">The stable eShopOnWeb reference of the owning customer.</param>
/// <param name="PlanId">The provider-assigned identifier of the current plan.</param>
/// <param name="PlanHandle">The stable handle of the current plan.</param>
/// <param name="PlanName">Display name of the current plan.</param>
/// <param name="PlanPriceInCents">Recurring price of the current plan, in cents.</param>
/// <param name="CurrentPeriodStartedAt">Start of the current billing period.</param>
/// <param name="CurrentPeriodEndsAt">End of the current billing period.</param>
/// <param name="NextAssessmentAt">When the provider will next assess (bill) this subscription.</param>
/// <param name="CancelAtEndOfPeriod">Whether an end-of-period cancellation is already pending.</param>
/// <param name="CanceledAt">When the subscription was cancelled, if it has been.</param>
/// <param name="NextPlanHandle">The handle of a plan change already scheduled for the next renewal.</param>
public record Subscription(
    int Id,
    SubscriptionState State,
    int CustomerId,
    string? CustomerReference,
    int PlanId,
    string PlanHandle,
    string PlanName,
    long PlanPriceInCents,
    DateTimeOffset? CurrentPeriodStartedAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextAssessmentAt,
    bool CancelAtEndOfPeriod,
    DateTimeOffset? CanceledAt,
    string? NextPlanHandle)
{
    /// <summary>The current plan's recurring price in the site's currency unit (dollars).</summary>
    public decimal PlanPrice => PlanPriceInCents / 100m;

    /// <summary>
    /// States in which the subscription is live enough to accept usage, a plan change, or a pause.
    /// </summary>
    private static readonly SubscriptionState[] LiveStates =
    {
        SubscriptionState.Active,
        SubscriptionState.Trialing,
        SubscriptionState.PastDue
    };

    /// <summary>Whether the subscription is currently billing the customer.</summary>
    public bool IsLive => LiveStates.Contains(State);

    /// <summary>
    /// Whether the requested lifecycle transition is legal from the current state. Illegal
    /// transitions are rejected locally so that no provider call is made (UC4).
    /// </summary>
    public bool CanTransitionTo(SubscriptionLifecycleAction action) => action switch
    {
        // Only a live subscription can be put on hold.
        SubscriptionLifecycleAction.Pause => State is SubscriptionState.Active or SubscriptionState.Trialing,

        // Resuming is only meaningful for a subscription that is actually on hold.
        SubscriptionLifecycleAction.Resume => State is SubscriptionState.Paused,

        // A subscription that has already ended cannot be cancelled again.
        SubscriptionLifecycleAction.Cancel => State is not (SubscriptionState.Canceled
            or SubscriptionState.Expired
            or SubscriptionState.Failed
            or SubscriptionState.Unknown),

        // Reactivation applies to subscriptions that have lapsed.
        SubscriptionLifecycleAction.Reactivate => State is SubscriptionState.Canceled
            or SubscriptionState.Expired
            or SubscriptionState.TrialEnded
            or SubscriptionState.Unpaid,

        _ => false
    };

    /// <summary>
    /// The lifecycle transitions that are legal right now — surfaced to the caller when an
    /// illegal transition is rejected, so the UI can explain what is possible instead.
    /// </summary>
    public IReadOnlyCollection<SubscriptionLifecycleAction> AllowedTransitions =>
        Enum.GetValues<SubscriptionLifecycleAction>().Where(CanTransitionTo).ToArray();
}
