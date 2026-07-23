using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A customer's subscription as reported by the billing provider. Monetary values are in whole
/// currency units (dollars). The provider is the system of record; this is a point-in-time view.
/// </summary>
public class BillingSubscription
{
    public BillingSubscription(int id, SubscriptionStatus status, string providerState)
    {
        Guard.Against.NullOrEmpty(providerState, nameof(providerState));

        Id = id;
        Status = status;
        ProviderState = providerState;
    }

    public int Id { get; }

    /// <summary>The normalized lifecycle state.</summary>
    public SubscriptionStatus Status { get; }

    /// <summary>The provider's raw state string, preserved for diagnostics and support.</summary>
    public string ProviderState { get; }

    public string? PlanHandle { get; init; }

    public string? PlanName { get; init; }

    /// <summary>The plan's recurring price in whole currency units (dollars).</summary>
    public decimal PlanPrice { get; init; }

    /// <summary>Outstanding balance in whole currency units (dollars).</summary>
    public decimal Balance { get; init; }

    public int CustomerId { get; init; }

    public string? CustomerReference { get; init; }

    public DateTimeOffset? CurrentPeriodStartsAt { get; init; }

    /// <summary>When the current billing period ends — the customer-facing "next billing date".</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    public DateTimeOffset? NextAssessmentAt { get; init; }

    public DateTimeOffset? CanceledAt { get; init; }

    /// <summary>True when a cancellation is scheduled for the end of the current period.</summary>
    public bool CancelAtEndOfPeriod { get; init; }

    public DateTimeOffset? DelayedCancelAt { get; init; }

    /// <summary>Set when a plan change is scheduled to take effect at the next renewal.</summary>
    public string? NextPlanHandle { get; init; }

    /// <summary>True when the subscription is live enough to accept usage and lifecycle actions.</summary>
    public bool IsLive => Status is SubscriptionStatus.Active or SubscriptionStatus.Trialing;
}
