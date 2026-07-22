using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A customer's subscription as the billing provider currently reports it. Money is in whole
/// currency units.
/// </summary>
public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; }
    public string CustomerReference { get; set; }
    public int CustomerId { get; set; }
    public int? PlanId { get; set; }
    public string PlanHandle { get; set; }
    public string PlanName { get; set; }
    public decimal PlanPrice { get; set; }
    public string Currency { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public bool CancelAtEndOfPeriod { get; set; }
    public DateTimeOffset? ScheduledCancellationAt { get; set; }
    public DateTimeOffset? OnHoldAt { get; set; }
    public DateTimeOffset? AutomaticallyResumeAt { get; set; }
    public string PendingPlanHandle { get; set; }
    public bool IsLive { get; set; }

    /// <summary>The lifecycle actions that are legal from the current state.</summary>
    public IReadOnlyList<string> AllowedActions { get; set; }
}
