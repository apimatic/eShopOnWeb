using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's Maxio subscription as returned to the caller.
/// </summary>
public class SubscriptionDto
{
    public long Id { get; set; }

    /// <summary>Maxio subscription state, e.g. "active", "trialing", "canceled".</summary>
    public string State { get; set; } = string.Empty;

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    /// <summary>Price in dollars per billing interval (from the subscribed product).</summary>
    public decimal Price { get; set; }

    public int Interval { get; set; }

    public string? IntervalUnit { get; set; }

    public string? Currency { get; set; }

    /// <summary>End of the current billing period.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When Maxio next assesses/bills the subscription.</summary>
    public DateTimeOffset? NextAssessmentAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }
}
