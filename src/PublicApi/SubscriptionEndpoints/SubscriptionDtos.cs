using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan (Maxio product) that a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    public long PlanId { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long? PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }

    /// <summary>Convenience price in the site currency's major unit (price_in_cents / 100).</summary>
    public decimal Price => PriceInCents.HasValue ? PriceInCents.Value / 100m : 0m;
}

/// <summary>
/// A subscription held by the current user (maxio subscription), including its plan.
/// </summary>
public class SubscriptionDto
{
    public long SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public SubscriptionPlanDto? Plan { get; set; }
    public long? BalanceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }

    /// <summary>The next date the subscriber will be billed.</summary>
    public DateTimeOffset? NextBillingDate => CurrentPeriodEndsAt ?? NextAssessmentAt;
}
