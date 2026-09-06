using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string PlanHandle { get; set; } = null!;
    public string PlanName { get; set; } = null!;
    public decimal PriceUSD { get; set; }
    public string State { get; set; } = null!;
    public DateTimeOffset? NextBillingDate { get; set; }
}
