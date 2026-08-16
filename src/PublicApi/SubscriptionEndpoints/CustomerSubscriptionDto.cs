using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's subscription as recorded by Maxio Advanced Billing.
/// </summary>
public class CustomerSubscriptionDto
{
    public int Id { get; set; }

    /// <summary>Subscription state, e.g. "active", "trialing", "canceled".</summary>
    public string State { get; set; } = string.Empty;

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    public long PriceInCents { get; set; }

    public string FormattedPrice { get; set; } = string.Empty;

    public int Interval { get; set; }

    public string? IntervalUnit { get; set; }

    /// <summary>When the next scheduled charge occurs (Maxio current_period_ends_at).</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset? NextAssessmentAt { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public int CustomerId { get; set; }

    public string? CustomerReference { get; set; }

    public string? Currency { get; set; }
}
