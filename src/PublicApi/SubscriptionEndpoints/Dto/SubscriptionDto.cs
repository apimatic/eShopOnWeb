using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A Maxio subscription as surfaced to the signed-in eShopOnWeb shopper.
/// </summary>
public class SubscriptionDto
{
    public long Id { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public long BalanceInCents { get; set; }

    /// <summary>End of the current billing period; the next renewal/billing date.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When Maxio will next attempt to assess/collect payment.</summary>
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
