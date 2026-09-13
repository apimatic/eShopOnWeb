using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse
{
    public int Id { get; set; }
    public string State { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string ProductHandle { get; set; } = "";
    public decimal Price { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
