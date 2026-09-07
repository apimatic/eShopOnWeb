using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class SubscriptionPlan : BaseEntity
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int MaxioProductId { get; set; }
    public DateTime LastSyncedAt { get; set; }
}
