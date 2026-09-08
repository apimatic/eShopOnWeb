using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Current state of a user's subscription, confirmed by Maxio Advanced Billing.
/// </summary>
public class SubscriptionSummaryDto
{
    public int SubscriptionId { get; set; }

    /// <summary>
    /// Maxio subscription state, e.g. "active".
    /// </summary>
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string? PlanName { get; set; }

    /// <summary>
    /// Recurring price per billing period (e.g. 299.00).
    /// </summary>
    public decimal Price { get; set; }
    public string? Currency { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }

    /// <summary>
    /// When the next billing run is scheduled.
    /// </summary>
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
}
