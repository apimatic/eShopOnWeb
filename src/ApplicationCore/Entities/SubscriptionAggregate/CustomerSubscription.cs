using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A customer's subscription as reported by the billing provider. eShopOnWeb runs the subscription
/// mapping statelessly, so this is a read-through projection rather than a persisted aggregate: the
/// provider is always the system of record.
/// </summary>
public sealed record CustomerSubscription
{
    public CustomerSubscription(int id, SubscriptionLifecycleState state)
    {
        Id = id;
        State = state;
    }

    public int Id { get; init; }

    public SubscriptionLifecycleState State { get; init; }

    /// <summary>The provider's raw state string, preserved for diagnostics and for states this build does not model.</summary>
    public string? ProviderState { get; init; }

    public string? PlanHandle { get; init; }

    public string? PlanName { get; init; }

    /// <summary>Current plan price per billing period, in dollars.</summary>
    public decimal PlanPrice { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    public DateTimeOffset? NextAssessmentAt { get; init; }

    public bool CancelAtEndOfPeriod { get; init; }

    public DateTimeOffset? DelayedCancelAt { get; init; }

    /// <summary>Set when a plan change has been scheduled for the next renewal.</summary>
    public string? ScheduledPlanHandle { get; init; }

    public int? CustomerId { get; init; }

    public string? CustomerReference { get; init; }

    /// <summary>The date the customer is next billed, if the provider reported one.</summary>
    public DateTimeOffset? NextBillingDate => NextAssessmentAt ?? CurrentPeriodEndsAt;

    /// <summary>True when the subscription is in a state that accrues metered usage.</summary>
    public bool IsBillable => State is SubscriptionLifecycleState.Active or SubscriptionLifecycleState.Trialing;
}
