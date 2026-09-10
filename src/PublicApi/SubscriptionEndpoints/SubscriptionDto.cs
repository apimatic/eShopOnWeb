using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Interval { get; set; } = "month";
    public int IntervalCount { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public string PaymentCollectionMethod { get; set; } = string.Empty;
}
