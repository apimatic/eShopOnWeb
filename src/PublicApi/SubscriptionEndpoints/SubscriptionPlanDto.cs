using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string? State { get; set; }
    public string? ProductName { get; set; }
    public string? ProductHandle { get; set; }
    public decimal Price { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
}
