using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A plan (Maxio product) a shopper can subscribe to.</summary>
public class SubscriptionPlanDto
{
    public long? Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public long? PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public string? ProductFamilyName { get; set; }
    public bool RequiresCreditCard { get; set; }
}
