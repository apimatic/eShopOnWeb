using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A customer's subscription as the billing provider currently reports it.
/// </summary>
public class SubscriptionDto
{
    public int Id { get; set; }
    public string Status { get; set; }
    public string? CustomerReference { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }

    /// <summary>Recurring price of the current plan, in whole currency units.</summary>
    public decimal PlanPrice { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public bool IsPendingCancellation { get; set; }
    public DateTimeOffset? DelayedCancelAt { get; set; }

    /// <summary>Set when a plan change is already scheduled for the next renewal.</summary>
    public string? ScheduledPlanHandle { get; set; }
}
