using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A customer's subscription as returned by the API.
/// </summary>
public class SubscriptionDto
{
    public int Id { get; set; }
    public string CustomerReference { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;

    /// <summary>The plan's recurring price in minor units (cents).</summary>
    public int PlanPriceInCents { get; set; }

    /// <summary>The plan's recurring price in major units (dollars).</summary>
    public decimal PlanPrice { get; set; }

    public string State { get; set; } = string.Empty;
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public bool CancelAtEndOfPeriod { get; set; }
    public DateTimeOffset? DelayedCancelAt { get; set; }
    public DateTimeOffset? AutomaticallyResumeAt { get; set; }
}
