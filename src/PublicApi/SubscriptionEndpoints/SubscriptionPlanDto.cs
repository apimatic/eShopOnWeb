using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public int? Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public long? PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    public decimal GetPrice() => PriceInCents.HasValue ? PriceInCents.Value / 100m : 0m;
}
