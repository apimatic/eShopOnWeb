using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription held by the authenticated caller, as reported by the billing system of record.
/// </summary>
public class SubscriptionDto
{
    public int Id { get; set; }

    /// <summary>Billing-system state, e.g. <c>active</c> or <c>trialing</c>.</summary>
    public string State { get; set; } = string.Empty;

    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public long? PriceInCents { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }

    /// <summary>When the current period ends and the next invoice is assessed.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}
