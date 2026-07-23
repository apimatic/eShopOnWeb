using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A customer's enrolment in a <see cref="SubscriptionPlan"/>, as reported by the billing provider.
/// </summary>
/// <remarks>
/// The billing provider is the system of record for subscriptions (plan.md §8 — the eShopOnWeb user
/// to provider-customer mapping is stateless and idempotent on <see cref="CustomerReference"/>), so this
/// type is an immutable read model rather than an EF-persisted entity. Numeric provider ids are valid for
/// the lifetime of a request only and are never written to the eShopOnWeb database.
/// </remarks>
public sealed record Subscription
{
    /// <summary>Provider-assigned subscription id.</summary>
    public required int Id { get; init; }

    /// <summary>Provider-assigned id of the owning customer.</summary>
    public int? CustomerId { get; init; }

    /// <summary>
    /// The eShopOnWeb user this subscription belongs to — the signed-in user's email/username
    /// (plan.md §4.4). This is the only value used to authorize customer-facing actions.
    /// </summary>
    public string? CustomerReference { get; init; }

    public required SubscriptionState State { get; init; }

    /// <summary>The raw state string the provider reported, kept for diagnostics.</summary>
    public string? ProviderState { get; init; }

    public string? PlanHandle { get; init; }

    public string? PlanName { get; init; }

    /// <summary>Recurring plan price in minor units (cents).</summary>
    public long PlanPriceInCents { get; init; }

    /// <summary>Outstanding balance in minor units (cents).</summary>
    public long BalanceInCents { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    public DateTimeOffset? NextAssessmentAt { get; init; }

    /// <summary>True when an end-of-period cancellation is already scheduled.</summary>
    public bool CancelAtEndOfPeriod { get; init; }

    public DateTimeOffset? ScheduledCancellationAt { get; init; }

    public DateTimeOffset? CanceledAt { get; init; }

    /// <summary>Handle of a plan change that is scheduled to take effect at the next renewal.</summary>
    public string? ScheduledPlanHandle { get; init; }

    public DateTimeOffset? PausedAt { get; init; }

    public DateTimeOffset? AutomaticallyResumeAt { get; init; }

    /// <summary>Recurring plan price in major units (dollars).</summary>
    public decimal PlanPrice => PlanPriceInCents / 100m;

    /// <summary>Outstanding balance in major units (dollars).</summary>
    public decimal Balance => BalanceInCents / 100m;

    /// <summary>
    /// True when the subscription is live enough to accept usage and plan changes.
    /// A paused subscription is deliberately excluded — it is live but not billing.
    /// </summary>
    public bool IsActive => State is SubscriptionState.Active or SubscriptionState.Trialing;

    /// <summary>
    /// True when the customer still holds this subscription in some form — used to make repeated
    /// subscribe attempts idempotent (plan.md UC1, "duplicate subscribe" failure scenario).
    /// </summary>
    public bool IsLive => State is SubscriptionState.Active
        or SubscriptionState.Trialing
        or SubscriptionState.Paused
        or SubscriptionState.PastDue
        or SubscriptionState.Pending;

    /// <summary>True when a plan change is scheduled for the next renewal.</summary>
    public bool HasScheduledPlanChange => !string.IsNullOrEmpty(ScheduledPlanHandle);
}
