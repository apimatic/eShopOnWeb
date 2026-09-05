namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

using System;

public class SubscriptionPlan
{
    public int Id { get; set; }
    public int MaxioProductId { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int IntervalValue { get; set; }
    public string IntervalUnit { get; set; } = "month";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
