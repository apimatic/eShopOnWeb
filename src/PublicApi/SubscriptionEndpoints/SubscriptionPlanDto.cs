using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string Handle { get; set; } = null!;
    public decimal Price { get; set; }
    public int BillingIntervalDays { get; set; }
    public string BillingIntervalUnit { get; set; } = null!;
    public string Description { get; set; } = null!;
}
