using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan offered to shoppers, as returned by the subscription endpoints.
/// </summary>
public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";
    public bool RequireCreditCard { get; set; }
}
