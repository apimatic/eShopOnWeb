using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }

    public string State { get; set; }

    public bool IsActive { get; set; }

    public string PlanHandle { get; set; }

    public string PlanName { get; set; }

    /// <summary>The plan price in whole currency units.</summary>
    public decimal PlanPrice { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? NextAssessmentAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public bool CancelAtEndOfPeriod { get; set; }

    public DateTimeOffset? DelayedCancelAt { get; set; }

    /// <summary>The plan a scheduled change will move to at the next renewal, if any.</summary>
    public string PendingPlanHandle { get; set; }

    /// <summary>The lifecycle transitions currently legal for this subscription.</summary>
    public string[] LegalActions { get; set; }
}
