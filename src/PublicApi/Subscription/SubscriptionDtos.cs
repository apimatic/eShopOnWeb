using System;

namespace Microsoft.eShopWeb.PublicApi.Subscription;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public string IntervalUnit { get; set; } = "month";
    public int Interval { get; set; }

    public decimal Price => PriceInCents / 100m;
}

public class CustomerSubscriptionDto
{
    public int SubscriptionId { get; set; }
    public int CustomerId { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }

    public decimal Price => PriceInCents / 100m;
}
