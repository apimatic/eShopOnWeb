using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public decimal? Price { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
}
