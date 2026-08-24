using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public DateTime? ActivatedAt { get; set; }

    /// <summary>
    /// When the current billing period ends and the next renewal charge is attempted.
    /// </summary>
    public DateTime? NextBillingAt { get; set; }
    public DateTime? CreatedAt { get; set; }
}
