using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; }
    public string Name { get; set; }
    public long PriceInCents { get; set; }
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; }
}
