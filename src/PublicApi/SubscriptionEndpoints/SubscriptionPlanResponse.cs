using System;
namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanResponse
{
    public int Id { get; set; }
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal PricePerMonth { get; set; }
    public string Currency { get; set; } = "USD";
}
