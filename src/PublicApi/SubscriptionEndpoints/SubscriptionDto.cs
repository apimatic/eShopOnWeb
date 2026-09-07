using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Handle { get; set; } = "";
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string ProductHandle { get; set; } = "";
    public string State { get; set; } = "";
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
}
