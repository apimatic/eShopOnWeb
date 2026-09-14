using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A Maxio subscription of the current user.</summary>
public class SubscriptionDto
{
    public long Id { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    /// <summary>Recurring amount in dollars (e.g. 299.00) charged each billing interval.</summary>
    public decimal? Price { get; set; }

    public string? Currency { get; set; }

    public int? IntervalCount { get; set; }

    /// <summary>Interval unit wire value, e.g. "month".</summary>
    public string? Interval { get; set; }

    /// <summary>Subscription state wire value, e.g. "active".</summary>
    public string? State { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? NextAssessmentAt { get; set; }

    /// <summary>When the next billing charge is expected (next assessment, else period end).</summary>
    public DateTimeOffset? NextBillingDate { get; set; }
}
