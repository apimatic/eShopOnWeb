namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// One of the signed-in shopper's subscriptions, as held by the billing system.
/// </summary>
public class SubscriptionSummaryDto
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public System.DateTimeOffset? NextBillingDate { get; set; }
    public System.DateTimeOffset? ActivatedAt { get; set; }
}
