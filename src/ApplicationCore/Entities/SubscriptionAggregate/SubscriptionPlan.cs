using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class SubscriptionPlan : BaseEntity, IAggregateRoot
{
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string? Interval { get; set; }
    public int? IntervalCount { get; set; }
    public int MaxioProductId { get; set; }
    public string? ProductFamilyHandle { get; set; }
}
