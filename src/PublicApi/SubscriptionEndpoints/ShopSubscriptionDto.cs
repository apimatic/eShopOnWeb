using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ShopSubscriptionDto
{
    public int Id { get; set; }
    public string ProductHandle { get; set; }
    public string ProductName { get; set; }
    public decimal? Price { get; set; }
    public string State { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
}
