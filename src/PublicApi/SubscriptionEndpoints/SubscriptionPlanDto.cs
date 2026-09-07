using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public decimal PriceInDollars => PriceInCents / 100m;
    public string? Description { get; set; }
}
