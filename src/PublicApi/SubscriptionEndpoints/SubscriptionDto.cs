using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A subscription as the billing provider currently reports it.</summary>
public class SubscriptionDto
{
    public int Id { get; set; }

    /// <summary>The normalised lifecycle state, for example <c>Active</c>.</summary>
    public string State { get; set; } = string.Empty;

    public string? CustomerReference { get; set; }
    public int? PlanId { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }

    /// <summary>The recurring plan price in major currency units.</summary>
    public decimal? PlanPrice { get; set; }

    /// <summary>The outstanding balance in major currency units.</summary>
    public decimal Balance { get; set; }

    public string? Currency { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public bool CancelAtEndOfPeriod { get; set; }
    public DateTimeOffset? ScheduledCancellationAt { get; set; }

    /// <summary>The plan a queued change will move this subscription to at the next renewal.</summary>
    public string? NextPlanHandle { get; set; }
}
