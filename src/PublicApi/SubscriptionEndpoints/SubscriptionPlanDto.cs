using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}

public class SubscriptionInfoDto
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string State { get; set; } = string.Empty;
    public decimal ProductPrice { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
}
