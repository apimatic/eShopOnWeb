using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A shopper's subscription as reported by Maxio.</summary>
public class SubscriptionDto
{
    public long? SubscriptionId { get; set; }
    public string? State { get; set; }
    public long? PlanId { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public decimal? Price { get; set; }
    public long? PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the next billing assessment is scheduled (next_assessment_at, falling back to current_period_ends_at).</summary>
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public bool? CancelAtEndOfPeriod { get; set; }
}
