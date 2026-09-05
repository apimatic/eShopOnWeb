using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class SubscriptionPlan : BaseEntity, IAggregateRoot
{
    public required string Handle { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required decimal PricePerMonth { get; set; }
    public int MaxioPlanId { get; set; }
    public bool IsAvailable { get; set; }
}
