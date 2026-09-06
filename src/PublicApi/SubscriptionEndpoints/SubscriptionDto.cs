using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string ProductName { get; set; } = null!;
    public string ProductHandle { get; set; } = null!;
    public decimal Price { get; set; }
    public string State { get; set; } = null!;
    public DateTime? NextBillingDate { get; set; }
}
