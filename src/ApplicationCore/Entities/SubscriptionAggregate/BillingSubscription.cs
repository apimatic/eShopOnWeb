using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A subscription as held by the billing provider, normalized into eShopOnWeb's own vocabulary.
/// The provider remains the system of record; this is a read-through projection of its state.
/// Monetary values are decimal currency units (dollars), never minor units (cents).
/// </summary>
public class BillingSubscription
{
    public int Id { get; init; }

    public SubscriptionState State { get; init; }

    /// <summary>The raw provider state string, retained for diagnostics when <see cref="State"/> is <see cref="SubscriptionState.Unknown"/>.</summary>
    public string? ProviderState { get; init; }

    public int CustomerId { get; init; }

    public string? CustomerReference { get; init; }

    public string? PlanHandle { get; init; }

    public string? PlanName { get; init; }

    /// <summary>Recurring plan price in decimal currency units.</summary>
    public decimal? PlanPrice { get; init; }

    /// <summary>Outstanding balance in decimal currency units.</summary>
    public decimal? Balance { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When the provider will next assess (bill) this subscription.</summary>
    public DateTimeOffset? NextAssessmentAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CanceledAt { get; init; }

    /// <summary>Set when an end-of-period cancellation has been scheduled.</summary>
    public DateTimeOffset? DelayedCancelAt { get; init; }

    public bool CancelAtEndOfPeriod { get; init; }

    /// <summary>Set when a plan change has been scheduled for the next renewal.</summary>
    public string? NextPlanHandle { get; init; }

    /// <summary>Set when a paused subscription is scheduled to resume automatically.</summary>
    public DateTimeOffset? AutomaticallyResumeAt { get; init; }

    /// <summary>True when the subscription is in a state that accrues usage and bills normally.</summary>
    public bool IsActive => State is SubscriptionState.Active or SubscriptionState.Trialing or SubscriptionState.Assessing;
}
