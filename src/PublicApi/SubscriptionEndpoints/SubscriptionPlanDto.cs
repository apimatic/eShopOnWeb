using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Handle { get; set; } = "";
    public string Description { get; set; } = "";
    public long PriceInCents { get; set; }
    public decimal PriceInDollars { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "";
    public bool RequireCreditCard { get; set; }
}
