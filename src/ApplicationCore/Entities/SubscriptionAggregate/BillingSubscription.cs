using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A subscription as the billing provider currently sees it. The provider is the system of record;
/// this is a normalised read model, never a locally mutated copy.
/// </summary>
/// <param name="Balance">Outstanding balance in major currency units (for example dollars).</param>
/// <param name="PlanPrice">Recurring plan price in major currency units.</param>
/// <param name="RawState">The provider's own state string, preserved for diagnostics.</param>
public sealed record BillingSubscription(
    int Id,
    BillingSubscriptionState State,
    string RawState,
    int? CustomerId,
    string? CustomerReference,
    int? PlanId,
    string? PlanHandle,
    string? PlanName,
    decimal? PlanPrice,
    decimal Balance,
    string? Currency,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextAssessmentAt,
    bool CancelAtEndOfPeriod,
    DateTimeOffset? ScheduledCancellationAt,
    string? NextPlanHandle)
{
    /// <summary>
    /// A state from which no further billing occurs and which cannot be paused, resumed or changed.
    /// An unrecognised state is deliberately not treated as terminated.
    /// </summary>
    public bool IsTerminated => State is BillingSubscriptionState.Canceled
        or BillingSubscriptionState.Expired
        or BillingSubscriptionState.FailedToCreate;

    /// <summary>Whether this subscription still represents a live enrolment for the customer.</summary>
    public bool IsLive => !IsTerminated;

    /// <summary>Whether the subscription is in a state that allows a plan change.</summary>
    public bool AllowsPlanChange => State is BillingSubscriptionState.Active or BillingSubscriptionState.Trialing;

    /// <summary>Whether the subscription is currently held/paused at the provider.</summary>
    public bool IsPaused => State is BillingSubscriptionState.OnHold or BillingSubscriptionState.Paused;
}
