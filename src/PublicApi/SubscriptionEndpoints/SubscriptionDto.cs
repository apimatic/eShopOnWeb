using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string State { get; set; } = string.Empty;
    public long ProductPriceInCents { get; set; }
    public decimal ProductPriceInDollars => ProductPriceInCents / 100m;
    public DateTimeOffset? NextBillingDate { get; set; }
}
