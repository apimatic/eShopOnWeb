using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A customer's subscription as the billing provider currently sees it. The provider is the system of
/// record; this type is a normalized read-model, never a locally mutated aggregate.
/// </summary>
public class CustomerSubscription
{
    public CustomerSubscription(int id,
        SubscriptionStatus status,
        string? customerReference,
        int? customerId,
        string? planHandle,
        string? planName,
        decimal planPrice,
        DateTimeOffset? currentPeriodStartedAt,
        DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? nextAssessmentAt,
        bool cancelAtEndOfPeriod,
        DateTimeOffset? delayedCancelAt,
        string? scheduledPlanHandle)
    {
        Id = id;
        Status = status;
        CustomerReference = customerReference;
        CustomerId = customerId;
        PlanHandle = planHandle;
        PlanName = planName;
        PlanPrice = planPrice;
        CurrentPeriodStartedAt = currentPeriodStartedAt;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextAssessmentAt = nextAssessmentAt;
        CancelAtEndOfPeriod = cancelAtEndOfPeriod;
        DelayedCancelAt = delayedCancelAt;
        ScheduledPlanHandle = scheduledPlanHandle;
    }

    public int Id { get; }

    public SubscriptionStatus Status { get; }

    /// <summary>The eShopOnWeb identity this subscription belongs to (email / username).</summary>
    public string? CustomerReference { get; }

    public int? CustomerId { get; }

    public string? PlanHandle { get; }

    public string? PlanName { get; }

    /// <summary>Recurring price of the current plan, in whole currency units.</summary>
    public decimal PlanPrice { get; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; }

    /// <summary>End of the current billing period — the customer-facing "next billing date".</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; }

    public DateTimeOffset? NextAssessmentAt { get; }

    public bool CancelAtEndOfPeriod { get; }

    public DateTimeOffset? DelayedCancelAt { get; }

    /// <summary>Non-null when a plan change has been scheduled for the next renewal.</summary>
    public string? ScheduledPlanHandle { get; }

    /// <summary>True when the subscription is scheduled to cancel at the end of the current period.</summary>
    public bool IsPendingCancellation => CancelAtEndOfPeriod || DelayedCancelAt.HasValue;

    /// <summary>True when usage may be reported and lifecycle actions other than reactivate are meaningful.</summary>
    public bool IsActive => Status is SubscriptionStatus.Active or SubscriptionStatus.Trialing
        or SubscriptionStatus.Assessing or SubscriptionStatus.PastDue or SubscriptionStatus.SoftFailure;

    /// <summary>True when the provider has the subscription on hold / paused.</summary>
    public bool IsPaused => Status is SubscriptionStatus.Paused;
}
