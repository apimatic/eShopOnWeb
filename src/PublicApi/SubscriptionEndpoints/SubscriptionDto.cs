using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }

    /// <summary>Lifecycle state, e.g. <c>Active</c> or <c>Paused</c>.</summary>
    public string State { get; set; }

    public string PlanHandle { get; set; }
    public string PlanName { get; set; }

    /// <summary>Plan price in major currency units.</summary>
    public decimal PlanPrice { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }

    /// <summary>Outstanding balance in major currency units.</summary>
    public decimal Balance { get; set; }

    public bool CancelAtEndOfPeriod { get; set; }
    public DateTimeOffset? ScheduledCancellationAt { get; set; }

    /// <summary>Plan scheduled to take effect at the next renewal, when a change is pending.</summary>
    public string PendingPlanHandle { get; set; }
}
